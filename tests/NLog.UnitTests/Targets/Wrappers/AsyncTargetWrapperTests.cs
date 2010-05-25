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

namespace NLog.UnitTests.Targets.Wrappers
{
    using System;
    using System.Threading;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using NLog.Internal;
    using NLog.Targets;
    using NLog.Targets.Wrappers;

    [TestClass]
    public class AsyncTargetWrapperTests : NLogTestBase
	{
        [TestMethod]
        public void AsyncTargetWrapperInitTest()
        {
            var myTarget = new MyTarget();
            var targetWrapper = new AsyncTargetWrapper(myTarget, 300, AsyncTargetWrapperOverflowAction.Grow);
            Assert.AreEqual(AsyncTargetWrapperOverflowAction.Grow, targetWrapper.OverflowAction);
            Assert.AreEqual(300, targetWrapper.QueueLimit);
            Assert.AreEqual(50, targetWrapper.TimeToSleepBetweenBatches);
            Assert.AreEqual(100, targetWrapper.BatchSize);
        }

        [TestMethod]
        public void AsyncTargetWrapperInitTest2()
        {
            var myTarget = new MyTarget();
            var targetWrapper = new AsyncTargetWrapper()
            {
                WrappedTarget = myTarget,
            };

            Assert.AreEqual(AsyncTargetWrapperOverflowAction.Discard, targetWrapper.OverflowAction);
            Assert.AreEqual(10000, targetWrapper.QueueLimit);
            Assert.AreEqual(50, targetWrapper.TimeToSleepBetweenBatches);
            Assert.AreEqual(100, targetWrapper.BatchSize);
        }

        [TestMethod]
        public void AsyncTargetWrapperSyncTest1()
        {
            var myTarget = new MyTarget();
            var targetWrapper = new AsyncTargetWrapper
            {
                WrappedTarget = myTarget,
            };
            ((ISupportsInitialize)targetWrapper).Initialize();

            var logEvent = new LogEventInfo();
            Exception lastException = null;
            ManualResetEvent continuationHit = new ManualResetEvent(false);
            Thread continuationThread = null;
            AsyncContinuation continuation =
                ex =>
                    {
                        lastException = ex;
                        continuationThread = Thread.CurrentThread;
                        continuationHit.Set();
                    };

            targetWrapper.WriteLogEvent(logEvent, continuation);

            // continuation was not hit 
            continuationHit.WaitOne();
            Assert.AreNotSame(continuationThread, Thread.CurrentThread);
            Assert.IsNull(lastException);
            Assert.AreEqual(1, myTarget.WriteCount);

            continuationHit.Reset();
            targetWrapper.WriteLogEvent(logEvent, continuation);
            continuationHit.WaitOne();
            Assert.AreNotSame(continuationThread, Thread.CurrentThread);
            Assert.IsNull(lastException);
            Assert.AreEqual(2, myTarget.WriteCount);
        }

        [TestMethod]
        public void AsyncTargetWrapperAsyncTest1()
        {
            var myTarget = new MyAsyncTarget();
            var targetWrapper = new AsyncTargetWrapper(myTarget);
            ((ISupportsInitialize)targetWrapper).Initialize();
            var logEvent = new LogEventInfo();
            Exception lastException = null;
            var continuationHit = new ManualResetEvent(false);
            AsyncContinuation continuation =
                ex =>
                {
                    lastException = ex;
                    continuationHit.Set();
                };

            targetWrapper.WriteLogEvent(logEvent, continuation);

            continuationHit.WaitOne();
            Assert.IsNull(lastException);
            Assert.AreEqual(1, myTarget.WriteCount);

            continuationHit.Reset();
            targetWrapper.WriteLogEvent(logEvent, continuation);
            continuationHit.WaitOne();
            Assert.IsNull(lastException);
            Assert.AreEqual(2, myTarget.WriteCount);
        }


        [TestMethod]
        public void AsyncTargetWrapperAsyncWithExceptionTest1()
        {
            var myTarget = new MyAsyncTarget
            {
                ThrowExceptions = true,
            };

            var targetWrapper = new AsyncTargetWrapper(myTarget);
            ((ISupportsInitialize)targetWrapper).Initialize();
            var logEvent = new LogEventInfo();
            Exception lastException = null;
            var continuationHit = new ManualResetEvent(false);
            AsyncContinuation continuation =
                ex =>
                {
                    lastException = ex;
                    continuationHit.Set();
                };

            targetWrapper.WriteLogEvent(logEvent, continuation);

            continuationHit.WaitOne();
            Assert.IsNotNull(lastException);
            Assert.IsInstanceOfType(lastException, typeof(InvalidOperationException));

            // no flush on exception
            Assert.AreEqual(0, myTarget.FlushCount);
            Assert.AreEqual(1, myTarget.WriteCount);

            continuationHit.Reset();
            lastException = null;
            targetWrapper.WriteLogEvent(logEvent, continuation);
            continuationHit.WaitOne();
            Assert.IsNotNull(lastException);
            Assert.IsInstanceOfType(lastException, typeof(InvalidOperationException));
            Assert.AreEqual(0, myTarget.FlushCount);
            Assert.AreEqual(2, myTarget.WriteCount);
        }

        class MyAsyncTarget : Target
        {
            public int FlushCount { get; private set; }
            public int WriteCount { get; private set; }

            protected override void Write(LogEventInfo logEvent)
            {
                throw new NotSupportedException();
            }

            protected override void Write(LogEventInfo logEvent, AsyncContinuation asyncContinuation)
            {
                Assert.IsTrue(this.FlushCount <= this.WriteCount);
                this.WriteCount++;
                ThreadPool.QueueUserWorkItem(
                    s =>
                        {
                            if (this.ThrowExceptions)
                            {
                                asyncContinuation(new InvalidOperationException("Some problem!"));
                                asyncContinuation(new InvalidOperationException("Some problem!"));
                            }
                            else
                            {
                                asyncContinuation(null);
                                asyncContinuation(null);
                            }
                        });
            }

            protected override void FlushAsync(AsyncContinuation asyncContinuation)
            {
                this.FlushCount++;
                ThreadPool.QueueUserWorkItem(
                    s => asyncContinuation(null));
            }

            public bool ThrowExceptions { get; set; }
        }

        class MyTarget : Target
        {
            public int FlushCount { get; set; }
            public int WriteCount { get; set; }

            protected override void Write(LogEventInfo logEvent)
            {
                Assert.IsTrue(this.FlushCount <= this.WriteCount);
                this.WriteCount++;
            }

            protected override void FlushAsync(AsyncContinuation asyncContinuation)
            {
                this.FlushCount++;
                asyncContinuation(null);
            }
        }
    }
}
