using System;
using System.Threading.Tasks;

using Speechly.SLUClient;

class Program
{
  static async Task Main(string[] args)
  {
    await SpeechlyClientTest.test("Speechly/SLUClientTest/00_chinese_restaurant.raw");
  }
}
