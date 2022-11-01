using System;
using System.Text;
using System.Threading.Tasks;
using Speechly.Types;
using System.IO;

namespace Speechly.Tools {

  public class Platform {
    private static bool debug = false;
    public static string ConfigPath = "Speechly";

    public static string GetDeviceId(string seed = null) {
      string deviceId;
      if (!String.IsNullOrEmpty(seed)) {
        deviceId = Platform.GuidFromString(seed);
        if (debug) Logger.Log($"Using manual deviceId: {deviceId}");
      } else {
        // Load settings
        Preferences config = Platform.RestoreOrCreateConfig<Preferences>(Preferences.FileName);
        // Restore or generate device id
        if (!String.IsNullOrEmpty(config.deviceId)) {
          deviceId = config.deviceId;
          if (debug) Logger.Log($"Restored deviceId: {deviceId}");
        } else {
          deviceId = System.Guid.NewGuid().ToString();
          config.deviceId = deviceId;
          Platform.SaveConfig<Preferences>(config, Preferences.FileName);
          if (debug) Logger.Log($"New deviceId: {deviceId}");
        }
      }
      return deviceId;
    }

    public static Task<byte[]> Fetch(string url) {

// Local files need to be fetched with file:// protocol on OS_X. On Android this won't work.
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
      if (url.IndexOf("://") < 0) {
        url = $"file://{url}";
      }
#endif

#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_ANDROID || UNITY_IOS
      var req = UnityEngine.Networking.UnityWebRequest.Get(url);
      var reqOp = req.SendWebRequest();
      var tsc = new TaskCompletionSource<byte[]>();

      void completeLoad() {
        if (!String.IsNullOrEmpty(req.error)) {
          throw new Exception($"Error while fetching from url {url}:\n{req.error}");
        }
        tsc.TrySetResult(reqOp.webRequest.downloadHandler.data);
      };

      if (reqOp.isDone) {
        completeLoad();
      } else {
        reqOp.completed += asyncOp => completeLoad();
      }

      return tsc.Task;

#else
      using (var client = new System.Net.Http.HttpClient()) {
        return client.GetByteArrayAsync(url);
      }
#endif
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

    public static T RestoreOrCreateConfig<T>(string ConfigFileName) where T: class, new() {
      string pathToFile = Path.Combine(Platform.GetPersistentStoragePath(), ConfigPath, ConfigFileName);
      T config = new T();
      try {
        string jsonString = File.ReadAllText(pathToFile, Encoding.UTF8);
        config = JSON.Parse(jsonString, config);
      } catch (Exception) {
      }
      return config;
    }

    public static void SaveConfig<T>(T configData, string ConfigFileName) {
      Directory.CreateDirectory(Path.Combine(Platform.GetPersistentStoragePath(), ConfigPath));

      string pathToFile = Path.Combine(Platform.GetPersistentStoragePath(), ConfigPath, ConfigFileName);
      File.WriteAllText(pathToFile, JSON.Stringify(configData), Encoding.UTF8);
    }

  }
}