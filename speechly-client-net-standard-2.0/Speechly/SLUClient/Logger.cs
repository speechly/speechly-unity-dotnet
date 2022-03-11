using System;

namespace Speechly.SLUClient {
  public class Logger {
    public delegate void LoggerDelegate(string s);
    public static LoggerDelegate Log = (string s) => {
      #if !ENABLE_MONO && !ENABLE_IL2CPP && !ENABLE_DOTNET
        Console.WriteLine(s);
      #endif
    };
  }
}