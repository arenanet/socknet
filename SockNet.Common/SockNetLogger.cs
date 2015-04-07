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
        /// Log leves.
        /// </summary>
        public enum LogLevel
        {
            DEBUG,
            INFO,
            ERROR
        }

        public static LogLevel LogSinkLevel { get; set; }

        public static LogSinkDelegate LogSink { get; set; }

        /// <summary>
        /// Initializes with default Console.Write logger and DEBUG sink level.
        /// </summary>
        static SockNetLogger()
        {
            LogSinkLevel = LogLevel.DEBUG;
            LogSink = (level, source, message, args) => 
            { 
                Console.WriteLine(string.Format("{0:s} - [{1}] ({2}) {3}", DateTime.Now, System.Enum.GetName(level.GetType(), level), source.GetType().Name, string.Format(message, args)));

                if (args != null && args.Length > 0)
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
        }

        /// <summary>
        /// Logs a message.
        /// </summary>
        /// <param name="level"></param>
        /// <param name="source"></param>
        /// <param name="message"></param>
        /// <param name="args"></param>
        public static void Log(LogLevel level, object source, string message, params object[] args)
        {
            if (LogSink != null && (int)level >= (int)LogSinkLevel)
            {
                LogSink(level, source, message, args);
            }
        }
    }
}
