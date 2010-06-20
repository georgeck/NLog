// 
// Copyright (c) 2004-2010 Jaroslaw Kowalski <jaak@jkowalski.net>
// 
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without 
// modification, are permitted provided that the following conditions 
// are met:
// 
// * Redistributions of source code must retain the above copyright notice, 
//   this list of conditions and the following disclaimer. 
// 
// * Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the following disclaimer in the documentation
//   and/or other materials provided with the distribution. 
// 
// * Neither the name of Jaroslaw Kowalski nor the names of its 
//   contributors may be used to endorse or promote products derived from this
//   software without specific prior written permission. 
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE 
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE 
// ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE 
// LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR 
// CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
// SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS 
// INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN 
// CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
// ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF 
// THE POSSIBILITY OF SUCH DAMAGE.
// 

#if !NET_CF && !SILVERLIGHT

namespace NLog.UnitTests.Targets.Wrappers
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.InteropServices;
    using System.Security.Principal;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using NLog.Common;
    using NLog.Targets;
    using NLog.Targets.Wrappers;

    [TestClass]
    public class ImpersonatingTargetWrapperTests : NLogTestBase
	{
        private const string NLogTestUser = "NLogTestUser";
        private const string NLogTestUserPassword = "BC57C01CCBC4";

        [TestMethod]
        public void ImpersonatingWrapperTest()
        {
            var wrapped = new MyTarget()
            {
                ExpectedUser = Environment.MachineName + "\\" + NLogTestUser,
            };

            var wrapper = new ImpersonatingTargetWrapper()
            {
                UserName = NLogTestUser,
                Password = NLogTestUserPassword,
                Domain = Environment.MachineName,
                WrappedTarget = wrapped,
            };

            // wrapped.Initialize();
            wrapper.Initialize();

            var exceptions = new List<Exception>();
            wrapper.WriteAsyncLogEvent(LogEventInfo.CreateNullEvent().WithContinuation(exceptions.Add));
            Assert.AreEqual(1, exceptions.Count);
            wrapper.WriteAsyncLogEvents(
                LogEventInfo.CreateNullEvent().WithContinuation(exceptions.Add),
                LogEventInfo.CreateNullEvent().WithContinuation(exceptions.Add),
                LogEventInfo.CreateNullEvent().WithContinuation(exceptions.Add));
            Assert.AreEqual(4, exceptions.Count);
            wrapper.Flush(exceptions.Add);
            Assert.AreEqual(5, exceptions.Count);
            foreach (var ex in exceptions)
            {
                Assert.IsNull(ex, Convert.ToString(ex));
            }

            wrapper.Close();
        }

        [TestMethod]
        public void RevertToSelfTest()
        {
            var wrapped = new MyTarget()
            {
                ExpectedUser = Environment.UserDomainName + "\\" + Environment.UserName,
            };

            WindowsIdentity originalIdentity = WindowsIdentity.GetCurrent();

            try
            {
                var id = this.CreateWindowsIdentity(NLogTestUser, Environment.MachineName, NLogTestUserPassword, SecurityLogonType.Interactive, LogonProviderType.Default, SecurityImpersonationLevel.Identification);
                id.Impersonate();

                WindowsIdentity changedIdentity = WindowsIdentity.GetCurrent();
                Assert.AreEqual(Environment.MachineName + "\\" + NLogTestUser, changedIdentity.Name);

                var wrapper = new ImpersonatingTargetWrapper()
                {
                    WrappedTarget = wrapped,
                    RevertToSelf = true,
                };

                // wrapped.Initialize();
                wrapper.Initialize();

                var exceptions = new List<Exception>();
                wrapper.WriteAsyncLogEvent(LogEventInfo.CreateNullEvent().WithContinuation(exceptions.Add));
                Assert.AreEqual(1, exceptions.Count);
                wrapper.WriteAsyncLogEvents(
                    LogEventInfo.CreateNullEvent().WithContinuation(exceptions.Add),
                    LogEventInfo.CreateNullEvent().WithContinuation(exceptions.Add),
                    LogEventInfo.CreateNullEvent().WithContinuation(exceptions.Add));
                Assert.AreEqual(4, exceptions.Count);
                wrapper.Flush(exceptions.Add);
                Assert.AreEqual(5, exceptions.Count);
                foreach (var ex in exceptions)
                {
                    Assert.IsNull(ex, Convert.ToString(ex));
                }

                wrapper.Close();
            }
            finally
            {
                // revert to self
                NativeMethods.RevertToSelf();

                WindowsIdentity currentIdentity = WindowsIdentity.GetCurrent();
                Assert.AreEqual(originalIdentity.Name, currentIdentity.Name);
            }
        }

        [TestMethod]
        public void ImpersonatingWrapperNegativeTest()
        {
            var wrapped = new MyTarget()
            {
                ExpectedUser = NLogTestUser,
            };

            var wrapper = new ImpersonatingTargetWrapper()
            {
                UserName = NLogTestUser,
                Password = Guid.NewGuid().ToString("N"), // wrong password
                Domain = Environment.MachineName,
                WrappedTarget = wrapped,
            };

            try
            {
                wrapper.Initialize();
                Assert.Fail("Expected exception");
            }
            catch (COMException)
            {
            }

            wrapper.Close(); // will not fail because Initialize() failed
        }

        [TestMethod]
        public void ImpersonatingWrapperNegativeTest2()
        {
            var wrapped = new MyTarget()
            {
                ExpectedUser = NLogTestUser,
            };

            var wrapper = new ImpersonatingTargetWrapper()
            {
                UserName = NLogTestUser,
                Password = NLogTestUserPassword, // wrong password
                Domain = Environment.MachineName,
                ImpersonationLevel = (SecurityImpersonationLevel)1234,
                WrappedTarget = wrapped,
            };

            try
            {
                wrapper.Initialize();
                Assert.Fail("Expected exception");
            }
            catch (COMException)
            {
            }

            wrapper.Close(); // will not fail because Initialize() failed
        }

        private WindowsIdentity CreateWindowsIdentity(string username, string domain, string password, SecurityLogonType logonType, LogonProviderType logonProviderType, SecurityImpersonationLevel impersonationLevel)
        {
            // initialize tokens
            var existingTokenHandle = IntPtr.Zero;
            var duplicateTokenHandle = IntPtr.Zero;

            if (!NativeMethods.LogonUser(
                username,
                domain,
                password,
                (int)logonType,
                (int)logonProviderType,
                out existingTokenHandle))
            {
                throw Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error());
            }

            if (!NativeMethods.DuplicateToken(existingTokenHandle, (int)impersonationLevel, out duplicateTokenHandle))
            {
                NativeMethods.CloseHandle(existingTokenHandle);
                throw Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error());
            }

            // create new identity using new primary token
            return new WindowsIdentity(duplicateTokenHandle);
        }

        private static class NativeMethods
        {
            // obtains user token
            [DllImport("advapi32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool LogonUser(string pszUsername, string pszDomain, string pszPassword, int dwLogonType, int dwLogonProvider, out IntPtr phToken);

            // closes open handes returned by LogonUser
            [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool CloseHandle(IntPtr handle);

            // creates duplicate token handle
            [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool DuplicateToken(IntPtr existingTokenHandle, int impersonationLevel, out IntPtr duplicateTokenHandle);

            [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool RevertToSelf();
        }

        public class MyTarget : Target
        {
            public MyTarget()
            {
                this.Events = new List<LogEventInfo>();
            }

            public List<LogEventInfo> Events { get; set; }

            public string ExpectedUser { get; set; }

            protected override void InitializeTarget()
            {
                base.InitializeTarget();
                this.AssertExpectedUser();
            }

            protected override void CloseTarget()
            {
                base.CloseTarget();
                this.AssertExpectedUser();
            }

            protected override void Write(LogEventInfo logEvent)
            {
                this.AssertExpectedUser();
                this.Events.Add(logEvent);
            }

            protected override void Write(AsyncLogEventInfo[] logEvents)
            {
                this.AssertExpectedUser();
                base.Write(logEvents);
            }

            protected override void FlushAsync(AsyncContinuation asyncContinuation)
            {
                this.AssertExpectedUser();
                base.FlushAsync(asyncContinuation);
            }

            private void AssertExpectedUser()
            {
                if (this.ExpectedUser != null)
                {
                    var windowsIdentity = WindowsIdentity.GetCurrent();
                    Assert.IsTrue(windowsIdentity.IsAuthenticated);
                    Assert.AreEqual(Environment.MachineName + "\\" + ExpectedUser, windowsIdentity.Name);
                }
            }
        }
    }
}

#endif