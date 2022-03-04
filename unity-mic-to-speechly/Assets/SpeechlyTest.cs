using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpeechlyTest : MonoBehaviour
{
    // Start is called before the first frame update
    async void Start()
    {
        Logger.Log = Debug.Log;
        await SpeechlyClientTest.test();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
