using System;
using System.IO;
using System.Text.Json;
using NUnit.Framework;

namespace BirimizKovulacak.Telemetry.Tests
{
    public class CatalogExportTests
    {
        static string RepoRoot()
        {
            string d = TestContext.CurrentContext.TestDirectory;
            while (d != null && !File.Exists(Path.Combine(d, "docs", "ANALYTICS_ARCHITECTURE.md"))) d = Path.GetDirectoryName(d);
            return d;
        }

        [Test]
        public void CatalogExport()
        {
            string json = TelemetryCatalogExport.ToJson();
            string md = TelemetryCatalogExport.ToMarkdown();
            var doc = JsonDocument.Parse(json);
            Assert.AreEqual(TelemetryCatalog.Count, doc.RootElement.GetProperty("events").GetArrayLength());
            StringAssert.Contains("pending_integration", md);
            StringAssert.Contains("### phase_exited", md);
            string root = RepoRoot();
            Assert.IsNotNull(root, "repository root not found");
            string jsonPath = Path.Combine(root, "docs", "analytics", "event_catalog.json");
            string mdPath = Path.Combine(root, "docs", "ANALYTICS_EVENT_CATALOG.md");
            if (Environment.GetEnvironmentVariable("TELEMETRY_EXPORT_DOCS") == "1")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(jsonPath));
                File.WriteAllText(jsonPath, json, new System.Text.UTF8Encoding(false));
                File.WriteAllText(mdPath, md, new System.Text.UTF8Encoding(false));
                Console.WriteLine("catalog exported: " + jsonPath);
            }
            else if (File.Exists(jsonPath))
            {
                Assert.AreEqual(Normalize(json), Normalize(File.ReadAllText(jsonPath)), "docs/analytics/event_catalog.json is stale: run with TELEMETRY_EXPORT_DOCS=1");
                Assert.AreEqual(Normalize(md), Normalize(File.ReadAllText(mdPath)), "docs/ANALYTICS_EVENT_CATALOG.md is stale: run with TELEMETRY_EXPORT_DOCS=1");
            }
        }

        static string Normalize(string s) { return s.Replace("\r\n", "\n"); }
    }
}
