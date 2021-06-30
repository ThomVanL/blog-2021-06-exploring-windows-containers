#include <iostream>
#include <windows.h>
#include <jobapi2.h>

#define MAX_THREADS 4

// Simulate some work later on
DWORD WINAPI SimulateWork(LPVOID lpParam);

int main()
{
    // A handle to be able to modify our Job
    HANDLE jobHandle = nullptr;

    // Structs that will hold some of the settings we will need.
    SECURITY_ATTRIBUTES securityAttributes;
    JOBOBJECT_CPU_RATE_CONTROL_INFORMATION cpuRateControlInfo;
    JOBOBJECT_BASIC_LIMIT_INFORMATION basicLimitInfo;
    JOBOBJECT_EXTENDED_LIMIT_INFORMATION extendedLimitInfo;

    // Initialize the structs.
    ZeroMemory(&securityAttributes, sizeof(SECURITY_ATTRIBUTES));
    ZeroMemory(&cpuRateControlInfo, sizeof(JOBOBJECT_CPU_RATE_CONTROL_INFORMATION));
    ZeroMemory(&basicLimitInfo, sizeof(JOBOBJECT_BASIC_LIMIT_INFORMATION));
    ZeroMemory(&extendedLimitInfo, sizeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));

    // Create a security attribute that has
    // a security descriptor associated with
    // the access token of the calling process.
    securityAttributes.nLength = sizeof(SECURITY_ATTRIBUTES);
    securityAttributes.lpSecurityDescriptor = nullptr;
    securityAttributes.bInheritHandle = 0;

    // Give the Job a name (pick one that will stand out) and
    // Associate the current process to the Job
    jobHandle = CreateJobObjectW(&securityAttributes, L"ThomasVanLaereExploringWindowsContainers");
    if (jobHandle == nullptr) {
        DWORD lastErr = GetLastError();
        fprintf(stderr, "Error value: %d Message: unable to create Job object.\n", lastErr);
        return lastErr;
    }

    BOOL assignProcessToJobObjectResult = AssignProcessToJobObject(
        jobHandle,
        GetCurrentProcess());

    if (!assignProcessToJobObjectResult) {
        DWORD lastErr = GetLastError();
        fprintf(stderr, "Error value: %d Message: unable to assign current process to Job object.\n", lastErr);
        return lastErr;
    }

    // We will set a hard CPU limit to 1,5% (1,5 x 100 CPU cycles == 150).
    // Processes within the job will not be able to exceed it (for long)
    JOBOBJECTINFOCLASS cpuRateinfoClass = JOBOBJECTINFOCLASS::JobObjectCpuRateControlInformation;
    cpuRateControlInfo.ControlFlags = JOB_OBJECT_CPU_RATE_CONTROL_ENABLE | JOB_OBJECT_CPU_RATE_CONTROL_HARD_CAP;
    cpuRateControlInfo.CpuRate = 150;
    BOOL setSetInformationJobObjectCpuRateControlResult = SetInformationJobObject(
        jobHandle,
        cpuRateinfoClass,
        &cpuRateControlInfo,
        sizeof(cpuRateControlInfo));

    if (!setSetInformationJobObjectCpuRateControlResult) {
        DWORD lastErr = GetLastError();
        fprintf(stderr, "Error value: %d Message: unable to set Job object CPU rate control.\n", lastErr);
        TerminateJobObject(jobHandle, lastErr);
    }

    // We can set multiple limits, so let's set:
    basicLimitInfo.LimitFlags = JOB_OBJECT_LIMIT_PRIORITY_CLASS | JOB_OBJECT_LIMIT_PROCESS_MEMORY;
    // - the priority class 'BELOW_NORMAL_PRIORITY'
    basicLimitInfo.PriorityClass = BELOW_NORMAL_PRIORITY_CLASS;
    // - the process memory limit to 20ish megabytes
    extendedLimitInfo.BasicLimitInformation = basicLimitInfo;
    extendedLimitInfo.ProcessMemoryLimit = 20971520;

    BOOL setSetInformationJobObjectExtendedLimitResult = SetInformationJobObject(
        jobHandle,
        JOBOBJECTINFOCLASS::JobObjectExtendedLimitInformation,
        &extendedLimitInfo,
        sizeof(extendedLimitInfo));

    if (!setSetInformationJobObjectExtendedLimitResult) {
        DWORD lastErr = GetLastError();
        fprintf(stderr, "Error value: %d Message: unable to set Job object priority class and max memory limit.\n", lastErr);
        TerminateJobObject(jobHandle, lastErr);
    }

    printf("Inspect this process via Sysinternals Process Explorer!\n");
    printf("Simulating work using %d threads.\n", MAX_THREADS);

    //Simulate some work
    for (int i = 0; i < MAX_THREADS; i++)
        CreateThread(NULL, 0, SimulateWork, NULL, 0, NULL);

    // Making sure our process does not get closed
    printf("\nPress enter to exit.\n");
    std::cin.get();

    // Clean up in case we hit enter
    if (jobHandle != nullptr) TerminateJobObject(jobHandle, 0);
}

// Do something completely trivial
DWORD WINAPI SimulateWork(LPVOID lpParam) {
    float calculation = 1.2345f;
    while (true)
    {
        calculation *= calculation;
    }
    return 0;
}
