using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;

namespace PluginSynode.Export
{
    public static class GlbUploader
    {
        public static void UploadGlb(string filePath, string uploadUrl, string apiKey)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("GLB file not found.", filePath);
            }

            using (var httpClient = new HttpClient())
            using (var form = new MultipartFormDataContent())
            using (var fileStream = File.OpenRead(filePath))
            using (var fileContent = new StreamContent(fileStream))
            {
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 SolidWorksGLBExporter/1.0");
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("model/gltf-binary");

                form.Add(fileContent, "file", Path.GetFileName(filePath));

                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    form.Add(new StringContent(apiKey), "api_key");
                }

                HttpResponseMessage response = httpClient.PostAsync(uploadUrl, form).GetAwaiter().GetResult();

                string responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception(
                        $"HTTP {(int)response.StatusCode}\n\n{responseBody}"
                    );
                }
            }
        }
    }
}
