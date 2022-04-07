using System;
using System.Text;

namespace Speechly.SLUClient {

  public class Platform {
    private static bool debug;

    public static string GetDeviceId(string seed = null) {
      string deviceId;
      if (!String.IsNullOrEmpty(seed)) {
        deviceId = Platform.GuidFromString(seed);
        if (debug) Logger.Log($"Using manual deviceId: {deviceId}");
      } else {
        // Load settings
        Preferences config = ConfigTool.RestoreOrCreate<Preferences>(Preferences.FileName);
        // Restore or generate device id
        if (!String.IsNullOrEmpty(config.deviceId)) {
          deviceId = config.deviceId;
          if (debug) Logger.Log($"Restored deviceId: {deviceId}");
        } else {
          deviceId = System.Guid.NewGuid().ToString();
          config.deviceId = deviceId;
          ConfigTool.Save<Preferences>(config, Preferences.FileName);
          if (debug) Logger.Log($"New deviceId: {deviceId}");
        }
      }
      return deviceId;
    }

    public static string GetPersistentStoragePath()
    {
#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_ANDROID || UNITY_IOS
      return UnityEngine.Application.persistentDataPath;
#else
      return System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
#endif
    }

    // Parse string and return a valid Guid, attempting to retain the original string if it's a valid Guid.
    // return D-type Guid string: 00000000-0000-0000-0000-000000000000
    public static string GuidFromString(string s) {
      try {
        // Attempt to parse string as Guid as-is
        return new Guid(s).ToString();
      } catch {
        // Guid will be created from the string bytes
        var bytes = Encoding.UTF8.GetBytes(s);
        Array.Resize<byte>(ref bytes, 16);
        return new Guid(bytes).ToString();
      }
    }
  }
}