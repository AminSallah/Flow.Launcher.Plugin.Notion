using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Flow.Launcher.Plugin.Notion
{
    

    internal class Toggl
    {

        private PluginInitContext _context { get; set; }

        internal Toggl(PluginInitContext context)
        {
            this._context = context;
        }
        


        /*static async Task Main()
        {
            await StartTimer("success test", new List<string>(), "CNS");
        }*/

        internal async Task StartTimer(string desc, List<string> tags, string projectName = "", string apiToken = "ce592d999a96693897307ce5671da77f")
        {

            string current_time = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            string url = "https://api.track.toggl.com/api/v9/time_entries";
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    var base64Token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiToken}:api_token"));
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", base64Token);

                    string tags_str = tags.Count > 0 ? string.Join("", tags) : "";

                    dynamic data = new ExpandoObject();
                    data.created_with = "API example code";
                    data.description = desc;
                    data.tags = new List<string> { tags_str };
                    data.billable = false;
                    data.workspace_id = 7361824;
                    data.duration = -1;
                    data.start = current_time;
                    data.stop = (string)null;

                    if (!string.IsNullOrEmpty(projectName))
                    {
                        ((IDictionary<string, object>)data)["pid"] = await GetProjectId(projectName, apiToken);
                    }

                    var jsonData = JsonSerializer.Serialize(data);
                    Console.WriteLine($"Request payload: {jsonData}");

                    var content = new StringContent(jsonData, Encoding.UTF8, "application/json");

                    var response = await client.PostAsync(url, content);

                    if (response.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        _context.API.ShowMsg("Timer has been started", $"{desc}");
                        Console.WriteLine("Request succeeded! Timer started.");
                    }
                    else
                        _context.API.ShowMsgError("Timer Error ", $"{response.StatusCode}");

                    {
                        Console.WriteLine($"Request failed with status code: {response.StatusCode}");
                    }
                }
            }
            catch
            {
                _context.API.ShowMsgError("Intialize Error ", $"Unexpected Error");

            }
        }

        static async Task<int> GetProjectId(string projectName, string apiToken)
        {
            string url = "https://api.track.toggl.com/api/v9/workspaces/7361824/projects";

            using (HttpClient client = new HttpClient())
            {
                var base64Token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiToken}:api_token"));
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", base64Token);
                client.DefaultRequestHeaders.Add("Notion-Version", "2022-06-28");

                var response = await client.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var projects = await response.Content.ReadFromJsonAsync<List<Project>>();
                    var project = projects?.Find(p => string.Equals(p.name, projectName, StringComparison.OrdinalIgnoreCase));

                    if (project != null)
                    {
                        return project.id;
                    }
                    else
                    {
                        // If the response is successful but the project is not found, create a new one
                        var projectData = new
                        {
                            active = true,
                            auto_estimates = false,
                            is_private = true,
                            name = projectName.Length <= 3 ? projectName.ToUpper() : char.ToUpper(projectName[0]) + projectName.Substring(1),
                        };

                        var projectContent = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(projectData), Encoding.UTF8, "application/json");

                        var createProjectResponse = client.PostAsync(url, projectContent).Result;

                        if (createProjectResponse.IsSuccessStatusCode)
                        {
                            var createdProject = createProjectResponse.Content.ReadFromJsonAsync<Project>().Result;
                            return createdProject.id;
                        }
                        else
                        {
                            return 0;
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"Failed to retrieve projects with status code: {response.StatusCode}");
                    return 0;
                }
            }
        }
    }

    class Project
    {
        public int id { get; set; }
        public string name { get; set; }
        // Add other properties as needed
    }

}