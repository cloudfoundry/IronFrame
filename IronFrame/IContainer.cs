﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace IronFrame
{
    public interface IContainer : IDisposable
    {
        string Id { get; }
        string Handle { get; }
        IContainerDirectory Directory { get; }
        ContainerInfo GetInfo();
        void Stop(bool kill);
        int ReservePort(int requestedPort);
        IContainerProcess Run(ProcessSpec spec, IProcessIO io);
        void LimitMemory(ulong limitInBytes);
        ulong CurrentMemoryLimit();
        void LimitCpu(int weight);
        int CurrentCpuLimit();
        void SetActiveProcessLimit(uint processLimit);
        void SetPriorityClass(ProcessPriorityClass priority);
        void LimitDisk(ulong limitInBytes);
        void SetProperty(string name, string value);
        string GetProperty(string name);
        Dictionary<string, string> GetProperties();
        void RemoveProperty(string name);
        void Destroy();
        void ImpersonateContainerUser(Action f);
        void StartGuard();
        void StopGuard();

        //ContainerState State { get; }
        //void BindMounts(IEnumerable<BindMount> mounts);
        //void CreateTarFile(string sourcePath, string tarFilePath, bool compress);
        //void CopyFileIn(string sourceFilePath, string destinationFilePath);
        //void CopyFileOut(string sourceFilePath, string destinationFilePath);
        //void ExtractTarFile(string tarFilePath, string destinationPath, bool decompress);
        IContainerProcess FindProcessById(int id);
        void CreateOutboundFirewallRule(FirewallRuleSpec firewallRuleSpec);
        ulong CurrentDiskLimit();
        ulong CurrentDiskUsage();
        ContainerMetrics GetMetrics();
    }

    public interface IProcessIO
    {
        TextWriter StandardOutput { get; }
        TextWriter StandardError { get; }
        TextReader StandardInput { get; }
    }
}
