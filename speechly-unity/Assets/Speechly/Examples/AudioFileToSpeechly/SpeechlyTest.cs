using UnityEngine;

using Speechly.SLUClient;
using Logger = Speechly.Tools.Logger;

public class SpeechlyTest : MonoBehaviour
{
  async void Start()
  {
    Logger.Log = Debug.Log;
    await SpeechlyClientTest.TestCloudProcessing("Assets/Speechly/Examples/AudioFileToSpeechly/SpeechlyClientTest/00_chinese_restaurant.raw");
  }
}
