using System;
using UnityEngine;
using System.Threading.Tasks;
using Speechly.Tools;

namespace Speechly.SLUClient {

  public class UnityOnDeviceDecoderFactory {

    public static OnDeviceDecoder CreateDecoder(string deviceId, string modelBundleFile, bool debug = false) {
      string path = $"{Application.streamingAssetsPath}/SpeechlyOnDevice/Models/";

#if UNITY_ANDROID || UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
      ByteBufProvider byteBufProvider = async() => {
        try {
          var bundleTask = Platform.Fetch($"{path}{modelBundleFile}");
          await Task.WhenAll(new []{bundleTask});
          return new ByteBufs{
            bundle_buf = bundleTask.Result
          };
        } catch (Exception e) {
          throw new Exception($"Failed to load Speechly on-device Model Bundle. Please check if the file exists.\n{e.Message}");
        }
      };

      return new OnDeviceDecoder(deviceId, byteBufProvider, debug);
#else
      throw new Exception("On-device decoding is not supported on this platform yet. Please contact Speechly.");
#endif
    }
  }

}
