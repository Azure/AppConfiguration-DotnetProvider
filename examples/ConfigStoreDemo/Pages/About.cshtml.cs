using System;
using System.Diagnostics;
using System.IO;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConfigStoreDemo.Pages
{
    public class AboutModel : PageModel
    {
        public string Message { get; set; }

        public void OnGet()
        {
            Trace.TraceError("******** Test ********");

            string instId = Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID");
            string hostname = Environment.GetEnvironmentVariable("COMPUTERNAME");
            string homePath = Environment.GetEnvironmentVariable("HOME");
            string targetFile = Path.Combine(homePath, "test.txt");
            if (System.IO.File.Exists(targetFile))
            {
                string time = System.IO.File.ReadAllText(targetFile);
                Message = $"Time = {time} --- hostname = {hostname} --- InstanceId = {instId}";
            }
            else
            {
                string time = DateTime.Now.ToString();
                System.IO.File.WriteAllText(targetFile, time);
                Message = $"Time = {time} --- hostname = {hostname} --- InstanceId = {instId}";
            }
        }
    }
}
