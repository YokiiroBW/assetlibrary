using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace AssetLibraryExplorerProof
{
    public sealed class ExecutionContextEvidence
    {
        public bool QuerySucceeded, Is64Bit, PackageIdentityAbsent, ParentIsSystemExplorer;
        public bool SameUser, SameSession, ParentPredatesExecutor, OrdinaryUser;
        public int PackageIdentityStatus;
        public uint ParentPid;
        public string ExecutorCreationFileTime, ParentCreationFileTime, ErrorType;
    }

    public static class ExecutionContextReader
    {
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] static extern uint GetProcessId(IntPtr handle);
        [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll")] static extern bool GetProcessTimes(IntPtr handle, out long created, out long exited, out long kernel, out long user);
        [DllImport("kernel32.dll")] static extern bool ProcessIdToSessionId(uint pid, out uint session);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr handle, uint flags, StringBuilder name, ref uint length);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetCurrentPackageFullName(ref uint length, IntPtr name);
        [DllImport("advapi32.dll")] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll")] static extern bool GetTokenInformation(IntPtr token, int information, out int value, int length, out int returned);

        public static ExecutionContextEvidence Read(uint parentPid)
        {
            var result = new ExecutionContextEvidence { ParentPid = parentPid, Is64Bit = Environment.Is64BitProcess };
            IntPtr parent = IntPtr.Zero, parentToken = IntPtr.Zero, selfToken = IntPtr.Zero;
            try
            {
                uint length = 0;
                result.PackageIdentityStatus = GetCurrentPackageFullName(ref length, IntPtr.Zero);
                result.PackageIdentityAbsent = result.PackageIdentityStatus == 15700;
                parent = OpenProcess(0x101000, false, parentPid); // Query limited information and hold identity until readback completes.
                Require(parent != IntPtr.Zero && GetProcessId(parent) == parentPid);
                IntPtr self = GetCurrentProcess();
                long parentCreated, selfCreated, exited, kernel, user;
                Require(GetProcessTimes(parent, out parentCreated, out exited, out kernel, out user));
                Require(GetProcessTimes(self, out selfCreated, out exited, out kernel, out user));
                result.ParentCreationFileTime = parentCreated.ToString();
                result.ExecutorCreationFileTime = selfCreated.ToString();
                result.ParentPredatesExecutor = parentCreated < selfCreated;
                var image = new StringBuilder(32768); length = (uint)image.Capacity;
                Require(QueryFullProcessImageName(parent, 0, image, ref length));
                string explorer = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                result.ParentIsSystemExplorer = string.Equals(image.ToString(), explorer, StringComparison.OrdinalIgnoreCase);
                uint parentSession, selfSession;
                Require(ProcessIdToSessionId(parentPid, out parentSession));
                Require(ProcessIdToSessionId(GetProcessId(self), out selfSession));
                result.SameSession = parentSession == selfSession;
                Require(OpenProcessToken(parent, 8, out parentToken) && OpenProcessToken(self, 8, out selfToken));
                using (var parentIdentity = new WindowsIdentity(parentToken))
                using (var selfIdentity = new WindowsIdentity(selfToken))
                {
                    result.SameUser = parentIdentity.User.Equals(selfIdentity.User);
                    int elevated, returned;
                    Require(GetTokenInformation(selfToken, 20, out elevated, 4, out returned) && returned == 4);
                    result.OrdinaryUser = elevated == 0 && !new WindowsPrincipal(selfIdentity).IsInRole(WindowsBuiltInRole.Administrator);
                }
                Require(WaitForSingleObject(parent, 0) == 258);
                result.QuerySucceeded = true;
            }
            catch (Exception error) { result.ErrorType = error.GetType().Name; }
            finally
            {
                if (selfToken != IntPtr.Zero) CloseHandle(selfToken);
                if (parentToken != IntPtr.Zero) CloseHandle(parentToken);
                if (parent != IntPtr.Zero) CloseHandle(parent);
            }
            return result;
        }

        static void Require(bool condition)
        {
            if (!condition) throw new InvalidOperationException("Execution context query incomplete");
        }
    }
}
