/*
 * Copyright 2015 ArenaNet, LLC.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not use this 
 * file except in compliance with the License. You may obtain a copy of the License at
 *
 * 	 http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under 
 * the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF 
 * ANY KIND, either express or implied. See the License for the specific language governing 
 * permissions and limitations under the License.
 */
using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.CompilerServices;

namespace ArenaNet.SockNet.Common
{
    public class SockNetLogger
    {
        /// <summary>
        /// A delegate that is used for logging.
        /// </summary>
        /// <param name="logLevel"></param>
        /// <param name="text"></param>
        public delegate void LogSinkDelegate(LogLevel logLevel, object source, string text, params object[] args);

        /// <summary>
        /// The default log sync which logs into console.
        /// </summary>
        public static readonly LogSinkDelegate DefaultLogSink = (level, source, message, args) => 
        { 
            Console.WriteLine(string.Format("{0:s} - [{1}] ({2}) {3}", DateTime.Now, System.Enum.GetName(level.GetType(), level), source.GetType().Name, string.Format(message, args)));

            if (level >= LogLevel.ERROR && args != null && args.Length > 0)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] is Exception)
                    {
                        Console.WriteLine("Exception thrown: " + ((Exception)args[i]).ToString());
                    }
                }
            }
        };
        
        /// <summary>
        /// Log leves.
        /// </summary>
        public enum LogLevel : byte
        {
            DEBUG = 0,
            INFO = 1,
            WARN = 2,
            ERROR = 3
        }

        /// <summary>
        /// The default log sink level.
        /// </summary>
        public static readonly LogLevel DefaultLogSinkLevel = LogLevel.DEBUG;

        /// <summary>
        /// The log level - this is at which level the logs will get sent to the sink, otherwise they will be thrown away.
        /// </summary>
        public static LogLevel LogSinkLevel { set { lock (_logSinkLevelLock) { _logSinkLevel = value; } } get { lock (_logSinkLevelLock) { return _logSinkLevel; } } }
        private static object _logSinkLevelLock = new object();
        private static LogLevel _logSinkLevel = DefaultLogSinkLevel;

        /// <summary>
        /// The log sink - this is where logs get sent.
        /// </summary>
        public static LogSinkDelegate LogSink { set { lock (_logSinkLock) { _logSink = value; } } get { lock (_logSinkLock) { return _logSink; } } }
        private static object _logSinkLock = new object();
        private static LogSinkDelegate _logSink = DefaultLogSink;

        /// <summary>
        /// Returns true if DEBUG is enabled.
        /// </summary>
        public static bool DebugEnabled { get { return LogLevel.DEBUG >= LogSinkLevel; } }

        /// <summary>
        /// Returns true if INFO is enabled.
        /// </summary>
        public static bool InfoEnabled { get { return LogLevel.INFO >= LogSinkLevel; } }

        /// <summary>
        /// Returns true if WARN is enabled.
        /// </summary>
        public static bool WarnEnabled { get { return LogLevel.WARN >= LogSinkLevel; } }

        /// <summary>
        /// Returns true if ERROR is enabled.
        /// </summary>
        public static bool ErrorEnabled { get { return LogLevel.ERROR >= LogSinkLevel; } }

        /// <summary>
        /// Logs a message.
        /// </summary>
        /// <param name="level"></param>
        /// <param name="source"></param>
        /// <param name="message"></param>
        /// <param name="args"></param>
        public static void Log(LogLevel level, object source, string message, params object[] args)
        {
            if (LogSink != null && level >= LogSinkLevel)
            {
                LogSink(level, source, message, args);
            }
        }
    }
}
