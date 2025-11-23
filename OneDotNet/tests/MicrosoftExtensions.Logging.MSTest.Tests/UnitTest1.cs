// Copyright (c) 2022 Zhang Shuai<zhangshuai.ustc@gmail.com>.
// All rights reserved.
//
// This file is part of OneDotNet.
//
// OneDotNet is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version.
//
// OneDotNet is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS
// FOR A PARTICULAR PURPOSE. See the GNU General Public License for more
// details.
//
// You should have received a copy of the GNU General Public License along with
// OneDotNet. If not, see <https://www.gnu.org/licenses/>.

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.Extensions.Logging.MSTest.Tests
{
    [TestClass]
    public sealed class UnitTest1 : IDisposable
    {
        private readonly LoggerFactory loggerFactory;

        public UnitTest1()
        {
            this.loggerFactory = new LoggerFactory(new[] { new MsTestLoggerProvider() });
        }

        [TestMethod]
        public void Test1()
        {
            var logger = this.loggerFactory.CreateLogger("Test1");
            logger.LogInformation("Hello World!");
            logger = this.loggerFactory.CreateLogger<UnitTest1>();
            logger.LogInformation("Hello World!");
        }

        [TestMethod]
        public void TestAllLogLevels()
        {
            var logger = this.loggerFactory.CreateLogger("TestAllLogLevels");

            logger.LogTrace("This is a trace message");
            logger.LogDebug("This is a debug message");
            logger.LogInformation("This is an information message");
            logger.LogWarning("This is a warning message");
            logger.LogError("This is an error message");
            logger.LogCritical("This is a critical message");
        }

        [TestMethod]
        public void TestLogWithException()
        {
            var logger = this.loggerFactory.CreateLogger("TestLogWithException");
            var exception = new InvalidOperationException("Test exception");

            logger.LogError(exception, "An error occurred: {Message}", exception.Message);
        }

        [TestMethod]
        public void TestLogWithEventId()
        {
            var logger = this.loggerFactory.CreateLogger("TestLogWithEventId");
            var eventId = new EventId(42, "TestEvent");

            logger.Log(LogLevel.Information, eventId, "Test message with event ID");
        }

        [TestMethod]
        public void TestLoggerProviderCreateLogger()
        {
            using var provider = new MsTestLoggerProvider();
            var logger = provider.CreateLogger("TestCategory");

            Assert.IsNotNull(logger);
            Assert.IsInstanceOfType<MsTestLogger>(logger);
        }

        [TestMethod]
        public void TestLoggerProviderCreateLoggerWithNullName()
        {
            using var provider = new MsTestLoggerProvider();

            Assert.ThrowsExactly<ArgumentNullException>(() => provider.CreateLogger(null));
        }

        [TestMethod]
        public void TestMsTestLoggerConstructor()
        {
            var logger = new MsTestLogger("TestLogger");

            Assert.AreEqual("TestLogger", logger.Name);
        }

        [TestMethod]
        public void TestMsTestLoggerConstructorWithNullName()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() => new MsTestLogger(null));
        }

        [TestMethod]
        public void TestIsEnabled()
        {
            var logger = new MsTestLogger("TestIsEnabled");

            // MSTest logger should always be enabled for all log levels
            Assert.IsTrue(logger.IsEnabled(LogLevel.Trace));
            Assert.IsTrue(logger.IsEnabled(LogLevel.Debug));
            Assert.IsTrue(logger.IsEnabled(LogLevel.Information));
            Assert.IsTrue(logger.IsEnabled(LogLevel.Warning));
            Assert.IsTrue(logger.IsEnabled(LogLevel.Error));
            Assert.IsTrue(logger.IsEnabled(LogLevel.Critical));
        }

        [TestMethod]
        public void TestBeginScope()
        {
            var logger = new MsTestLogger("TestBeginScope");

            using var scope = logger.BeginScope("Test scope");
            Assert.IsNotNull(scope);
        }

        public void Dispose()
        {
            this.loggerFactory.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
