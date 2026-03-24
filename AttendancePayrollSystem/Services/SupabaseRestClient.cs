using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AttendancePayrollSystem.Services
{
    public static class SupabaseRestClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly Lazy<HttpClient> Client = new(CreateClient, true);

        public static List<T> GetList<T>(string resource, IDictionary<string, string>? query = null)
        {
            using var request = CreateRequest(HttpMethod.Get, resource, query);
            using var response = Send(request);
            return DeserializeList<T>(response);
        }

        public static T? GetSingleOrDefault<T>(string resource, IDictionary<string, string>? query = null)
        {
            var results = GetList<T>(resource, query);
            return results.FirstOrDefault();
        }

        public static T InsertAndReturnSingle<T>(string resource, object payload)
        {
            return SendForSingle<T>(HttpMethod.Post, resource, payload, null);
        }

        public static T UpdateAndReturnSingle<T>(string resource, object payload, IDictionary<string, string> query)
        {
            return SendForSingle<T>(HttpMethod.Patch, resource, payload, query);
        }

        public static void Update(string resource, object payload, IDictionary<string, string> query)
        {
            using var request = CreateRequest(HttpMethod.Patch, resource, query, payload);
            request.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
            using var _ = Send(request);
        }

        public static void Delete(string resource, IDictionary<string, string> query)
        {
            using var request = CreateRequest(HttpMethod.Delete, resource, query);
            using var _ = Send(request);
        }

        private static T SendForSingle<T>(HttpMethod method, string resource, object payload, IDictionary<string, string>? query)
        {
            using var request = CreateRequest(method, resource, query, payload);
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            using var response = Send(request);
            var results = DeserializeList<T>(response);
            if (results.Count == 0)
            {
                throw new InvalidOperationException($"Supabase API returned no rows for {resource}.");
            }

            return results[0];
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient
            {
                BaseAddress = SupabaseConfig.RestApiUrl,
                Timeout = TimeSpan.FromSeconds(30)
            };

            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.TryAddWithoutValidation("apikey", SupabaseConfig.PublishableKey);
            return client;
        }

        private static HttpRequestMessage CreateRequest(
            HttpMethod method,
            string resource,
            IDictionary<string, string>? query = null,
            object? payload = null)
        {
            var request = new HttpRequestMessage(method, BuildRelativeUri(resource, query));

            if (payload != null)
            {
                var json = JsonSerializer.Serialize(payload, JsonOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            return request;
        }

        private static string BuildRelativeUri(string resource, IDictionary<string, string>? query)
        {
            if (query == null || query.Count == 0)
            {
                return resource;
            }

            var queryString = string.Join(
                "&",
                query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

            return $"{resource}?{queryString}";
        }

        private static HttpResponseMessage Send(HttpRequestMessage request)
        {
            var response = Client.Value.SendAsync(request).GetAwaiter().GetResult();
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var details = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            throw new InvalidOperationException(BuildErrorMessage(response, details));
        }

        private static List<T> DeserializeList<T>(HttpResponseMessage response)
        {
            using var stream = response.Content.ReadAsStream();
            return JsonSerializer.Deserialize<List<T>>(stream, JsonOptions) ?? new List<T>();
        }

        private static string BuildErrorMessage(HttpResponseMessage response, string details)
        {
            if (string.IsNullOrWhiteSpace(details))
            {
                return $"Supabase API request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}).";
            }

            return $"Supabase API request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}). Details: {details}";
        }
    }
}
