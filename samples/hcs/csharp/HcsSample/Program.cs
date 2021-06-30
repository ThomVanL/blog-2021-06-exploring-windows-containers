using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Windows.ComputeVirtualization;
using Microsoft.Windows.ComputeVirtualization.Schema;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HcsSample
{
    enum StandardStreams
    {
        StdIn = 0,
        StdOut = 1,
        StdErr = 2
    }

    class Program
    {
        private static event EventHandler<string> _serverContainerProcessStdOutPublishedEvent;
        private static event EventHandler<string> _serverContainerProcessStdErrPublishEvent;

        private static StringBuilder _stringBuilder;
        private static object _lockObj;

        private static Process _serverContainerProcess;

        static async Task Main(string[] args)
        {
            _stringBuilder = new StringBuilder(512);
            _lockObj = new object();

            const string command = @"cmd.exe /k dir C:\ & whoami";
            const string imageName = "mcr.microsoft.com/windows/servercore:ltsc2019";

            Guid containerId = Guid.NewGuid();

            string scratchLayerNewPath = Path.Combine(@"C:\", "temp", containerId.ToString());
            string baseImageDir = string.Empty;
            Guid hnsNetworkId;

            try
            {
                hnsNetworkId = HostComputeService.FindNatNetwork();
                Console.Out.WriteLine($"Found network '{hnsNetworkId}' with network mode 'NAT'");
            }
            catch (Exception)
            {
                Console.Error.WriteLine($"Host Networking Service could not find any networks with network mode 'NAT'.");
                return;
            }


            using (IDockerClient client = new DockerClientConfiguration().CreateClient())
            {
                ImageInspectResponse imageInfo = await client.Images.InspectImageAsync(imageName);
                if (imageInfo == null)
                {
                    Console.Error.WriteLine("Image not found.");
                    return;
                }


                baseImageDir = imageInfo.GraphDriver.Data["dir"];
                if (!imageInfo.GraphDriver.Data.TryGetValue("dir", out baseImageDir))
                {
                    Console.Error.WriteLine("Directory associated with image not found.");
                    return;
                }
            }

            string imageLayerChainPath = Path.Combine(baseImageDir, "layerchain.json");
            if (!File.Exists(imageLayerChainPath))
            {
                Console.Error.WriteLine("Layerchain.json associated with layer not found");
                return;
            }

            string imageLayerChainData = File.ReadAllText(imageLayerChainPath);
            IList<string> imageLayerParentLayerPath = JsonConvert.DeserializeObject<List<string>>(imageLayerChainData);

            string parentLayer = imageLayerParentLayerPath.Last();
            if (parentLayer == null)
            {
                Console.Error.WriteLine("Layerchain.json associated with *parent* layer not found.");
                return;
            }

            IList<Layer> containerLayers = new List<Layer>
            {
                new Layer
                {
                    Id = containerId,
                    Path = parentLayer.ToString()
                }
            };


            ContainerStorage.CreateSandbox(scratchLayerNewPath, containerLayers);
            Console.Out.WriteLine($"Created sandbox.VHDX at '{scratchLayerNewPath}'.");

            try
            {
                ContainerSettings containerSettings = new ContainerSettings
                {
                    HyperVContainer = false,
                    KillOnClose = true,
                    Layers = containerLayers,
                    MappedDirectories = null,
                    NetworkId = hnsNetworkId,
                    SandboxPath = scratchLayerNewPath,
                    UtilityVmPath = string.Empty
                };
                using (Container serverContainer = HostComputeService.CreateContainer(containerId.ToString(), containerSettings))
                {
                    serverContainer.Start();
                    try
                    {
                        ProcessStartInfo processStartInfo = new ProcessStartInfo
                        {
                            CommandLine = command,
                            KillOnClose = true,
                            EmulateConsole = false,
                            RedirectStandardOutput = true,
                            RedirectStandardInput = true,
                            RedirectStandardError = true,
                        };

                        Console.Out.WriteLine($"Executing command: {command}");

                        using (_serverContainerProcess = serverContainer.CreateProcess(processStartInfo))
                        {
                            _serverContainerProcess.StandardInput.AutoFlush = true;

                            if (_serverContainerProcess.StandardOutput != null)
                            {
                                _serverContainerProcessStdOutPublishedEvent += OnServerContainerProcessStdOutPublished;
                                Thread stdoutThread = new Thread(() => ReadStdStream(StandardStreams.StdOut)) { IsBackground = true };
                                stdoutThread.Start();
                            }

                            if (_serverContainerProcess.StandardError != null)
                            {
                                _serverContainerProcessStdErrPublishEvent += OnServerContainerProcessStdErrPublished;
                                Thread stdErrThread = new Thread(() => ReadStdStream(StandardStreams.StdErr)) { IsBackground = true };
                                stdErrThread.Start();
                            }

                            _ = Task.Run(() => _serverContainerProcess.WaitForExitAsync());

                            while (true)
                            {
                                try
                                {
                                    if (_serverContainerProcess.ExitCode >= 0)
                                        break;
                                }
                                catch (Exception) { }

                                if (_serverContainerProcess.StandardInput != null)
                                    _serverContainerProcess.StandardInput.WriteLine(Console.ReadLine());

                                await Task.Delay(100);
                            }
                        }
                    }
                    finally
                    {
                        Console.Out.WriteLine($"Process exited with code {_serverContainerProcess.ExitCode }.");
                        serverContainer.Shutdown(Timeout.Infinite);
                        Console.Out.WriteLine($"Container {containerId} shut down.");
                    }
                }
            }
            finally
            {
                ContainerStorage.DestroyLayer(scratchLayerNewPath);
                Console.Out.WriteLine($"Scratch layer removed: {scratchLayerNewPath}");

            }

            Console.Out.WriteLine("Press enter to exit..");
            Console.ReadLine();
        }



        private static void ReadStdStream(StandardStreams streamType)
        {
            try
            {
                StreamReader targetStream;
                switch (streamType)
                {
                    case StandardStreams.StdOut:
                        targetStream = _serverContainerProcess.StandardOutput;
                        break;
                    case StandardStreams.StdErr:
                        targetStream = _serverContainerProcess.StandardError;
                        break;
                    case StandardStreams.StdIn:
                    default:
                        throw new NotImplementedException();
                }

                int currentCharacter;
                while (_serverContainerProcess != null && (currentCharacter = targetStream.Read()) > -1)
                {
                    ReadStream(targetStream, streamType, (char)currentCharacter);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error processing std stream {streamType}: \n" + ex.Message);
            }
        }

        private static void ReadStream(StreamReader streamReader, StandardStreams streamType, char currentCharacter)
        {
            lock (_lockObj)
            {
                char nextCharacter;
                const char newLineChar = '\n';
                const char carriageReturn = '\r';

                _stringBuilder.Clear();
                _stringBuilder.Append(currentCharacter);

                while (streamReader.Peek() > -1)
                {
                    nextCharacter = (char)streamReader.Read();
                    int secondToLastIndex = _stringBuilder.Length - 1;

                    //Check if next characters are '\r\n'
                    if (_stringBuilder.Length > 0 && nextCharacter != newLineChar && _stringBuilder[secondToLastIndex] == carriageReturn)
                    {
                        FlushStringBuilder(_stringBuilder, streamType);
                    }

                    _stringBuilder.Append(nextCharacter);

                    //Check if next character is '\n'
                    if (nextCharacter == newLineChar)
                    {
                        FlushStringBuilder(_stringBuilder, streamType);
                    }
                }
                FlushStringBuilder(_stringBuilder, streamType);
            }
        }

        private static void FlushStringBuilder(StringBuilder stringBuilder, StandardStreams streamType)
        {
            if (stringBuilder.Length > 0)
            {
                try
                {
                    switch (streamType)
                    {
                        case StandardStreams.StdOut when _serverContainerProcessStdOutPublishedEvent != null:
                            _serverContainerProcessStdOutPublishedEvent(streamType, stringBuilder.ToString());
                            break;
                        case StandardStreams.StdErr when _serverContainerProcessStdErrPublishEvent != null:
                            _serverContainerProcessStdErrPublishEvent(streamType, stringBuilder.ToString());
                            break;
                    }
                }
                catch (Exception) { }
                finally
                {
                    stringBuilder.Clear();
                }
            }
        }
        private static void OnServerContainerProcessStdErrPublished(object sender, string output)
        {
            Console.Error.Write(output);
        }

        private static void OnServerContainerProcessStdOutPublished(object sender, string output)
        {
            Console.Out.Write(output);
        }
    }
}
