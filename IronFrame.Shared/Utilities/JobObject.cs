﻿using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using IronFrame.Win32;
using Microsoft.Win32.SafeHandles;

namespace IronFrame.Utilities
{
    internal class CpuStatistics
    {
        public TimeSpan TotalKernelTime { get; set; }
        public TimeSpan TotalUserTime { get; set; }
    }

    internal class JobObject : IDisposable
    {
        SafeJobObjectHandle handle;
        SafeFileHandle completionPort;

        public JobObject()
            : this(null)
        {
        }

        public JobObject(string name)
            : this(name, false)
        {
        }

        public JobObject(string name, bool openExisting, bool terminateOnLastHandleClose = true)
        {
            if (openExisting)
            {
                handle = new SafeJobObjectHandle(NativeMethods.OpenJobObject(NativeMethods.JobObjectAccessRights.AllAccess, false, name));
            }
            else
            {
                var token = NativeMethods.CreateJobObject(IntPtr.Zero, name);
                if (token == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                handle = new SafeJobObjectHandle(token);
                SetJobLimits(NativeMethods.JobObjectLimit.KillOnJobClose);
                SetCompletionPort();
            }

            if (handle.IsInvalid)
            {
                throw new Exception("Unable to create job object.");
            }
        }

        public SafeJobObjectHandle Handle
        {
            get { return handle; }
        }

        public virtual void Dispose()
        {
            if (handle != null)
            {
                handle.Dispose();
                handle = null;
            }

            if (completionPort != null)
            {
                completionPort.Dispose();
                completionPort = null;
            }
        }

        public virtual void AssignProcessToJob(IntPtr processHandle)
        {
            NativeMethods.AssignProcessToJobObject(handle, processHandle);
        }

        public virtual void AssignProcessToJob(Process process)
        {
            AssignProcessToJob(process.Handle);
        }

        public virtual CpuStatistics GetCpuStatistics()
        {
            if (handle == null) { throw new ObjectDisposedException("JobObject"); }

            var info = GetJobObjectBasicAccountingInformation(handle);

            return new CpuStatistics
            {
                TotalKernelTime = new TimeSpan(info.TotalKernelTime),
                TotalUserTime = new TimeSpan(info.TotalUserTime),
            };
        }

        public virtual ulong GetJobMemoryLimit()
        {
            var extendedLimit = GetJobLimits();
            if (extendedLimit.BasicLimitInformation.LimitFlags.HasFlag(NativeMethods.JobObjectLimit.JobMemory))
            {
                return extendedLimit.JobMemoryLimit.ToUInt64();
            }

            return 0;
        }

        public virtual ulong GetPeakJobMemoryUsed()
        {
            var extendedLimit = GetJobLimits();
            if (extendedLimit.BasicLimitInformation.LimitFlags.HasFlag(NativeMethods.JobObjectLimit.JobMemory))
            {
                return extendedLimit.PeakJobMemoryUsed.ToUInt64();
            }

            return 0;
        }

        public virtual int[] GetProcessIds()
        {
            return GetJobObjectProcessIds(handle);
        }

        static NativeMethods.JobObjectBasicAccountingInformation GetJobObjectBasicAccountingInformation(SafeJobObjectHandle handle)
        {
            int infoSize = Marshal.SizeOf(typeof(NativeMethods.JobObjectBasicAccountingInformation));
            IntPtr infoPtr = IntPtr.Zero;
            try
            {
                infoPtr = Marshal.AllocHGlobal(infoSize);

                if (!NativeMethods.QueryInformationJobObject(
                    handle,
                    NativeMethods.JobObjectInfoClass.JobObjectBasicAccountingInformation,
                    infoPtr,
                    infoSize,
                    IntPtr.Zero))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error != NativeMethods.Constants.ERROR_MORE_DATA)
                        throw new Win32Exception(error);
                }

                return (NativeMethods.JobObjectBasicAccountingInformation)Marshal.PtrToStructure(infoPtr, typeof(NativeMethods.JobObjectBasicAccountingInformation));
            }
            finally
            {
                if (infoPtr != IntPtr.Zero)
                    Marshal.FreeHGlobal(infoPtr);
            }
        }

        private NativeMethods.JobObjectExtendedLimitInformation GetJobLimits()
        {
            int length = Marshal.SizeOf(typeof(NativeMethods.JobObjectExtendedLimitInformation));
            IntPtr extendedInfoPtr = IntPtr.Zero;
            try
            {
                extendedInfoPtr = Marshal.AllocHGlobal(length);

                if (!NativeMethods.QueryInformationJobObject(
                        handle,
                        NativeMethods.JobObjectInfoClass.ExtendedLimitInformation,
                        extendedInfoPtr,
                        length,
                        IntPtr.Zero))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                var extendedLimit = (NativeMethods.JobObjectExtendedLimitInformation)Marshal.PtrToStructure(extendedInfoPtr, typeof(NativeMethods.JobObjectExtendedLimitInformation));

                return extendedLimit;
            }
            finally
            {
                if (extendedInfoPtr != IntPtr.Zero)
                    Marshal.FreeHGlobal(extendedInfoPtr);
            }
        }

        static int[] GetJobObjectProcessIds(SafeJobObjectHandle handle)
        {
            const int JobCountIncrement = 5;

            int numberOfAssignedProcessesOffset = Marshal.OffsetOf(typeof(NativeMethods.JobObjectBasicProcessIdList), "NumberOfAssignedProcesses").ToInt32();
            int numberOfProcessIdsInListOffset = Marshal.OffsetOf(typeof(NativeMethods.JobObjectBasicProcessIdList), "NumberOfProcessIdsInList").ToInt32();
            int firstProcessIdOffset = Marshal.OffsetOf(typeof(NativeMethods.JobObjectBasicProcessIdList), "FirstProcessId").ToInt32();

            int numberOfProcessesInJob = JobCountIncrement;
            do
            {
                int infoSize = firstProcessIdOffset + (IntPtr.Size * numberOfProcessesInJob);
                IntPtr infoPtr = IntPtr.Zero;
                try
                {
                    infoPtr = Marshal.AllocHGlobal(infoSize);
                    NativeMethods.FillMemory(infoPtr, (IntPtr)infoSize, 0);

                    Marshal.WriteInt32(infoPtr, numberOfAssignedProcessesOffset, numberOfProcessesInJob);
                    Marshal.WriteInt32(infoPtr, numberOfProcessIdsInListOffset, 0);


                    if (!NativeMethods.QueryInformationJobObject(
                        handle,
                        NativeMethods.JobObjectInfoClass.JobObjectBasicProcessIdList,
                        infoPtr,
                        infoSize,
                        IntPtr.Zero))
                    {
                        var error = Marshal.GetLastWin32Error();
                        if (error == NativeMethods.Constants.ERROR_MORE_DATA)
                        {
                            numberOfProcessesInJob += JobCountIncrement;
                            continue;
                        }

                        throw new Win32Exception(error);
                    }

                    int count = Marshal.ReadInt32(infoPtr, numberOfProcessIdsInListOffset);
                    if (count == 0)
                        return new int[0];

                    IntPtr[] ids = new IntPtr[count];

                    Marshal.Copy(infoPtr + firstProcessIdOffset, ids, 0, count);

                    return ids.Select(id => id.ToInt32()).ToArray();
                }
                finally
                {
                    if (infoPtr != IntPtr.Zero)
                        Marshal.FreeHGlobal(infoPtr);
                }

            } while (true);
        }

        public enum CompletionMsg
        {
            AbnormalExitProcess = 8,
            ActiveProcessLimit = 3,
            ActiveProcessZero = 4,
            EndOfJobTime = 1,
            EndOfProcessTime = 2,
            ExitProcess = 7,
            JobCycleTimeLimit = 12,
            JobMemoryLimit = 10,
            NewProcess = 6,
            NotificationLimit = 11,
            ProcessMemoryLimit = 9,
            NoCompletionStatus = 0
        }

        public System.Collections.Generic.IEnumerable<CompletionMsg> GetQueuedCompletionStatus()
        {
            if (completionPort != null)
            {
                IntPtr completionKey;
                IntPtr overlapped;
                uint cc;
                const uint completionWaitMs = 10;
                while (NativeMethods.GetQueuedCompletionStatus(
                    completionPort.DangerousGetHandle(),
                    out cc,
                    out completionKey,
                    out overlapped,
                    completionWaitMs))
                {
                    yield return (CompletionMsg) cc;
                }
            }
        }

        public NativeMethods.JobObjectLimitViolationInformation GetLimitViolationInformation()
        {
            using (var allocation = SafeAllocation.Create<NativeMethods.JobObjectLimitViolationInformation>())
            {
                if (!NativeMethods.QueryInformationJobObject(handle, NativeMethods.JobObjectInfoClass.LimitViolationInformation, allocation.DangerousGetHandle(), allocation.Size, IntPtr.Zero))
                    throw Win32LastError("Unable to query limit violation information");

                return allocation.ToStructure();
            }
        }

        public void SetCompletionPort()
        {
            int length = Marshal.SizeOf(typeof(NativeMethods.JobObjectAssociateCompletionPort));
            IntPtr completionPortPtr = IntPtr.Zero;
            try
            {
                var nativeCompletionPort = NativeMethods.CreateIoCompletionPort(new IntPtr(-1), IntPtr.Zero, 0, 0);
                completionPort = new SafeFileHandle(nativeCompletionPort, true);

                var cp = new NativeMethods.JobObjectAssociateCompletionPort
                {
                    CompletionKey = IntPtr.Zero,
                    CompletionPortHandle = completionPort.DangerousGetHandle(),
                };

                completionPortPtr = Marshal.AllocHGlobal(length);

                Marshal.StructureToPtr(cp, completionPortPtr, false);

                if (
                    !NativeMethods.SetInformationJobObject(handle,
                        NativeMethods.JobObjectInfoClass.AssociateCompletionPortInformation, completionPortPtr, length))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            catch (Win32Exception)
            {
                completionPort = null;
            }
            finally
            {
                if (completionPortPtr != IntPtr.Zero)
                    Marshal.FreeHGlobal(completionPortPtr);
            }
        }

        private void SetJobLimits(NativeMethods.JobObjectLimit limitFlag)
        {
            var extendedLimit = GetJobLimits();
            extendedLimit.BasicLimitInformation.LimitFlags |= limitFlag;
            SetJobLimits(extendedLimit);
        }

        private void SetJobNotificationLimits(NativeMethods.JobObjectNotificationLimitInformation notificationLimit)
        {
            using (var allocation = SafeAllocation.Create(notificationLimit))
            {
                if (!NativeMethods.SetInformationJobObject(handle, NativeMethods.JobObjectInfoClass.NotificationLimitInformation, allocation.DangerousGetHandle(), allocation.Size))
                    throw Win32LastError("Unable to set limit violation information");
            }
        }

        private void SetJobLimits(NativeMethods.JobObjectExtendedLimitInformation extendedLimit)
        {
            int length = Marshal.SizeOf(typeof(NativeMethods.JobObjectExtendedLimitInformation));
            IntPtr extendedInfoPtr = IntPtr.Zero;
            try
            {
                extendedInfoPtr = Marshal.AllocHGlobal(length);

                Marshal.StructureToPtr(extendedLimit, extendedInfoPtr, false);

                if (!NativeMethods.SetInformationJobObject(handle, NativeMethods.JobObjectInfoClass.ExtendedLimitInformation, extendedInfoPtr, length))
                {
                    throw new Exception(string.Format("Unable to set information.  Error: {0}", Marshal.GetLastWin32Error()));
                }
            }
            finally
            {
                if (extendedInfoPtr != IntPtr.Zero)
                    Marshal.FreeHGlobal(extendedInfoPtr);
            }
        }

        private void SetUIRestrictions(NativeMethods.UIRestrictions restrictionsFlag)
        {
            var restrictions = new NativeMethods.JobObjectUIRestrictions
            {
                UIRestrictionsClass = restrictionsFlag
            };
            using (var allocation = SafeAllocation.Create(restrictions))
            {
                if (!NativeMethods.SetInformationJobObject(handle, NativeMethods.JobObjectInfoClass.BasicUIRestrictions, allocation.DangerousGetHandle(), allocation.Size))
                {
                    throw new Exception(string.Format("Unable to set information.  Error: {0}", Marshal.GetLastWin32Error()));
                }
            }
        }

        public virtual void SetJobMemoryLimit(ulong jobMemoryLimitInBytes)
        {
            var extendedLimit = GetJobLimits();

            extendedLimit.BasicLimitInformation.LimitFlags |= NativeMethods.JobObjectLimit.JobMemory;
            extendedLimit.JobMemoryLimit = new UIntPtr(jobMemoryLimitInBytes);

            SetJobLimits(extendedLimit);
        }

        public virtual void SetActiveProcessLimit(uint activeProcessLimit)
        {
            var extendedLimit = GetJobLimits();

            extendedLimit.BasicLimitInformation.LimitFlags |= NativeMethods.JobObjectLimit.ActiveProcess;
            extendedLimit.BasicLimitInformation.ActiveProcessLimit = activeProcessLimit;

            SetJobLimits(extendedLimit);
        }

        public virtual void TerminateProcesses()
        {
            if (handle == null) { throw new ObjectDisposedException("JobObject"); }
            NativeMethods.TerminateJobObject(handle, 0);
        }

        public virtual void TerminateProcessesAndWait(int milliseconds = System.Threading.Timeout.Infinite)
        {
            TerminateProcesses();
            var pids = GetProcessIds();
            foreach (var pid in pids)
            {
                var process = Process.GetProcessById(pid);
                process.WaitForExit(milliseconds);
            }
            using (var waitHandle = new JobObjectWaitHandle(handle))
            {
                waitHandle.WaitOne(milliseconds);
            }
        }

        //
        // P/Invoke Helpers
        //

        static Exception Win32LastError(string message, params object[] args)
        {
            var error = Marshal.GetLastWin32Error();
            return new Win32Exception(
                error,
                String.Format(message, args) +
                String.Format(" (Win32 Error Code {0})", error));
        }


        public virtual void SetJobCpuLimit(int cpuWeight)
        {
            if (cpuWeight < 1 || cpuWeight > 9)
            {
                throw new ArgumentOutOfRangeException("cpuWeight", cpuWeight, "CPU Limit must be between 1 and 9");
            }
            var cpuRateInfo = new NativeMethods.JobObjectCpuRateControlInformation
            {
                ControlFlags = (UInt32)(NativeMethods.JobObjectCpuRateControl.Enable | NativeMethods.JobObjectCpuRateControl.WeightBased),
                Weight = (uint) cpuWeight,
            };
            using (var allocation = SafeAllocation.Create<NativeMethods.JobObjectCpuRateControlInformation>(cpuRateInfo))
            {
                if (!NativeMethods.SetInformationJobObject(handle, NativeMethods.JobObjectInfoClass.CpuRateControlInformation, allocation.DangerousGetHandle(), allocation.Size))
                    throw Win32LastError("Unable to query Cpu rate information");
            }
        }

        public virtual int GetJobCpuLimit()
        {
            using (var allocation = SafeAllocation.Create<NativeMethods.JobObjectCpuRateControlInformation>())
            {
                if (!NativeMethods.QueryInformationJobObject(handle, NativeMethods.JobObjectInfoClass.CpuRateControlInformation, allocation.DangerousGetHandle(), allocation.Size, IntPtr.Zero))
                    throw Win32LastError("Unable to query Cpu rate information");

                return (int)allocation.ToStructure().CpuRate;
            }
        }

        public virtual void SetPriorityClass(ProcessPriorityClass c)
        {
            var extendedLimit = GetJobLimits();

            extendedLimit.BasicLimitInformation.LimitFlags |= NativeMethods.JobObjectLimit.PriorityClass;
            extendedLimit.BasicLimitInformation.PriorityClass = (uint)c;

            SetJobLimits(extendedLimit);
        }
    }
}
