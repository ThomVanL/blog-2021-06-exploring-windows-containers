# Exploring Windows Containers

## Jobs, a couple of Silos and the Host Compute Service.

A little over a year ago I started writing about what makes a Linux container. My motivation for doing so was because I thought, and still believe, that the " why" is more often discussed as opposed to the “what”. Mind you, I do not think it always necessary to delve into the “what” as long as you’re capable of being productive. 🙃

I've always felt very curious to learn about of the technical underpinnings of Windows containers. These details oftentimes abstracted away from the user, usually for good reason. At any rate, I've wanted to create an equivalent to the "[Exploring (Linux) Containers](https://thomasvanlaere.com/categories/exploring-linux-containers/)" articles, to highlight some of the things that make this technology possible.

> 💡 The samples that are discussed in the article were built and tested on an Azure Virtual Machine that supports nested virtualization, with the latest Visual Studio on __Windows Server 2019__ image from the Azure Marketplace. For more information, have a glance at the following [Azure Docs page](https://docs.microsoft.com/en-us/azure/virtual-machines/windows/using-visual-studio-vm).

Feel free to read the [full blog post](https://thomasvanlaere.com/posts/2021/06/exploring-windows-containers/)!

## Samples

- Applying resource controls using Job objects (C++)
- Talk to HCS via PowerShell
- Talk to HCS via .NET language binding with PowerShell
  - Using [dotnet-computevirtualization](https://github.com/microsoft/dotnet-computevirtualization)
- Talk to the HCS via .NET languages binding with C# (with stdin, stdout and stderr redirection)

## Prerequisites

All samples are known to function with:

- PowerShell 5.1+
- Windows Server 2019
- Visual Studio Community 2019

⚠️ The "_Invoke-HcsProcessIsolationSample.ps1_" sample requires a .NET standard build of the HCS .NET language binding ([dotnet-computevirtualization](https://github.com/microsoft/dotnet-computevirtualization)) to function. The language binding has been included as a Git submodule.