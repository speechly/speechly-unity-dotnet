using System.Threading.Tasks;
using System.Net.Http;
using System.Runtime.Serialization.Json;  
using System;  
using System.IO;  
using System.Text;
using System.Diagnostics;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8604 // Possible null reference argument for parameter 'projectId'

public class AppTokenRequest
{
    public string appId;
    public string deviceId;
}

public class ProjectTokenRequest
{
    public string projectId;
    public string deviceId;
}


public class TokenResponse
{
    public string access_token;
}

class LoginToken {
  public async Task<string> fetchToken(
    string baseUrl,
    string projectId,
    string appId,
    string deviceId
  ) {
    var httpClient = new HttpClient();
    HttpResponseMessage postResponse;

    string json;
    if (projectId != null) {
      var body = new ProjectTokenRequest{
        projectId = projectId,
        deviceId = deviceId
      };
      json = JSON.JSONSerialize(body);
      // postResponse = await httpClient.PostAsJsonAsync(baseUrl, body);
    } else {
      var body = new AppTokenRequest{
        appId = appId,
        deviceId = deviceId
      };
      json = JSON.JSONSerialize(body);
    }
    // Log(json);
    // Log(baseUrl);
    var postRequest = new HttpRequestMessage(HttpMethod.Post, baseUrl);
    postRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");
      // var u = JSONDeserialize(json, new AppTokenRequest());
    try {
      postResponse = await httpClient.SendAsync(postRequest);
    } catch ( Exception ) {
      throw new Exception($"Problem fetching auth token from '{baseUrl}'." );
    }

    postResponse.EnsureSuccessStatusCode();

    if (!(postResponse.Content is object && postResponse.Content.Headers.ContentType.MediaType == "application/json")) {
      throw new Exception("HTTP Response was invalid and cannot be deserialised.");
    }

    // Log(postResponse.Headers.ToString());
    var contentStream = await postResponse.Content.ReadAsStreamAsync();
    TokenResponse tokenResponse;
    try {
      tokenResponse = JSON.JSONDeserializeStream(contentStream, new TokenResponse());
    } catch (Exception) {
      throw new Exception("Invalid JSON.");
    }

    if (tokenResponse == null || tokenResponse.access_token == null) {
      throw new Exception("The response did not contain an access token.");
    }

    return tokenResponse.access_token;
  }

  public async Task test() {
    // See https://aka.ms/new-console-template for more information
    Logger.Log("Fetching token...");

    var loginUrl = "https://api.speechly.com/login";
    string projectId = null;
    var appId = "c9f51b42-626c-4557-93a9-93cb205141d9";
    var deviceId = System.Guid.NewGuid().ToString();

    Stopwatch stopWatch = new Stopwatch();
    stopWatch.Start();
    string token = "";
    token = await fetchToken(loginUrl, projectId, appId, deviceId);
    stopWatch.Stop();
    
    TimeSpan ts = stopWatch.Elapsed;
    string elapsedTime = String.Format("{0:00}:{1:00}:{2:00}.{3:00}",
            ts.Hours, ts.Minutes, ts.Seconds,
            ts.Milliseconds / 10);
    Logger.Log($"Time elapsed is {elapsedTime}");


    Logger.Log($"The token is {token}");
  }
}
