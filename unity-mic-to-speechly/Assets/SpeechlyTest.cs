using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpeechlyTest : MonoBehaviour
{
    async void Start()
    {
        Logger.Log = Debug.Log;
        await SpeechlyClientTest.test();
    }
}
