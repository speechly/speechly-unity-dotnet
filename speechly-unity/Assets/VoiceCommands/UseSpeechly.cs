using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Speechly.SLUClient;
using UnityEngine.UI;
using TMPro;
using System.Linq;

namespace Speechly.Demo.VoiceCommands
{

  public class UseSpeechly : MonoBehaviour
  {
    public Camera Cam;
    public GameObject Cube;
    public GameObject Sphere;
    public Slider SliderAudioPeak;
    public TMP_Text TranscriptText;
    private SpeechlyClient speechlyClient;
    private bool IsButtonHeld = false;

    void Start()
    {
      speechlyClient = MicToSpeechly.Instance.SpeechlyClient;
      speechlyClient.OnSegmentChange += (segment) =>
      {
        Debug.Log(segment.ToString());
        TranscriptText.text = segment.ToString(
          (intent) => "",
          (words, entityType) => $"<color=#ffc0ff>{words.ToUpper()}<color=#ffffff>",
          ""
        );

        if (selection != null) {
          var size = segment.entities.Select(entry => entry.Value).ToList().Where(entity => entity.type == "size").Select(entity => entity.value).ToArray().FirstOrDefault();
          //.Where(entity => entity.type == "size").Select(entity => entity.value).First();
          if (size == "big") selection.localScale = new Vector3(2,2,2);
          if (size == "normal") selection.localScale = new Vector3(1,1,1);
          if (size == "small") selection.localScale = new Vector3(0.5f,0.5f,0.5f);
          if (size == "tall") selection.localScale = new Vector3(0.5f,10f,0.5f);

          // Set visibility
          selection.gameObject.SetActive(segment.intent.intent != "delete");
        }
      };
    }

    private Transform selection;

    async void Update()
    {
      SliderAudioPeak.value = MicToSpeechly.Instance.Peak;

      if (Input.GetMouseButtonDown(0)) {
        Debug.Log("Button press");

        if (!speechlyClient.IsListening) {
          await speechlyClient.StartContext();
        }

        RaycastHit hit;
        Ray ray = Cam.ScreenPointToRay(Input.mousePosition);
        
        if (Physics.Raycast(ray, out hit)) {
          selection = hit.transform;
          Debug.Log(selection);
            // Do something with the object that was hit by the raycast.
        } else {
          selection = null;
        }
      }

      if (Input.GetMouseButtonUp(0)) {
        Debug.Log("Button release");
        if (speechlyClient.IsListening) {
          await speechlyClient.StopContext();
        }

      }

    }

    public async void OnMouseDown()
    {
      if (!IsButtonHeld && !speechlyClient.IsListening)
      {
        await speechlyClient.StartContext();
        IsButtonHeld = true;
        Debug.Log("Mouse Down");
      }
    }

    public async void OnMouseUp()
    {
      if (IsButtonHeld && speechlyClient.IsListening)
      {
        Debug.Log("Mouse Up");
        IsButtonHeld = false;
        await speechlyClient.StopContext();
      }
    }

  }
}