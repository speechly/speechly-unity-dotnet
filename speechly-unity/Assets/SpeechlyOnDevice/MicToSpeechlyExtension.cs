namespace Speechly.SLUClient {

  partial class MicToSpeechly {
    partial void CreateOnDeviceDecoder(string deviceId, string modelBundleFile, bool debug) {
      this.decoder = UnityOnDeviceDecoderFactory.CreateDecoder(deviceId, modelBundleFile, debug);
    }
  }

}