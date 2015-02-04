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
        public delegate void LogSinkDelegate(LogLevel logLevel, string text, params object[] args);
        
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
            LogSink = (level, message, args) => { Console.WriteLine(System.Enum.GetName(level.GetType(), level) + " - " + string.Format(message, args)); };
        }

        /// <summary>
        /// Logs a message.
        /// </summary>
        /// <param name="level"></param>
        /// <param name="message"></param>
        /// <param name="args"></param>
        public static void Log(LogLevel level, string message, params object[] args)
        {
            if (LogSink != null && (int)level >= (int)LogSinkLevel)
            {
                LogSink(level, message, args);
            }
        }
    }
}
