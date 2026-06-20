using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using UnityEngine;

/// <summary>
/// Development utility — lists all objects in the furniture S3 bucket
/// and exports the result to a CSV file on disk.
/// </summary>
public class S3BucketInspector : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private bool runOnStart = true;
    [SerializeField] private string bucketName = AwsConfig.FurnitureBucketName;

    async void Start()
    {
        if (runOnStart)
            await ListAllObjects();
    }

    public async Task ListAllObjects()
    {
        if (AwsManager.Instance == null || !AwsManager.Instance.IsInitialized)
        {
            Debug.LogWarning("[S3BucketInspector] AWS not ready. Waiting 3 seconds...");
            await Task.Delay(3000);
        }

        if (AwsManager.Instance?.S3Client == null)
        {
            Debug.LogError("[S3BucketInspector] S3Client not available.");
            return;
        }

        try
        {
            Debug.Log($"[S3BucketInspector] Scanning bucket: {bucketName}");

            var request = new ListObjectsV2Request
            {
                BucketName = bucketName
            };

            var rows = new List<string>();
            rows.Add("type,key,folder,filename,extension,size_bytes,size_readable,last_modified");

            int totalCount = 0;
            int glbCount   = 0;

            ListObjectsV2Response response;

            do
            {
                response = await AwsManager.Instance.S3Client
                    .ListObjectsV2Async(request);

                foreach (var obj in response.S3Objects)
                {
                    string key       = obj.Key;
                    long   sizeBytes = obj.Size ?? 0;
                    string modified = obj.LastModified.HasValue 
                        ? obj.LastModified.Value.ToString("yyyy-MM-dd HH:mm:ss") 
                        : "";
                    string ext       = Path.GetExtension(key).ToLower();
                    string filename  = Path.GetFileName(key);
                    string folder    = Path.GetDirectoryName(key)?.Replace("\\", "/") ?? "";

                    string type;
                    if (key.EndsWith("/"))
                        type = "FOLDER";
                    else if (ext == ".glb" || ext == ".gltf")
                    {
                        type = "MODEL";
                        glbCount++;
                    }
                    else if (ext == ".jpg" || ext == ".jpeg"
                             || ext == ".png" || ext == ".webp")
                        type = "IMAGE";
                    else if (ext == ".fbx" || ext == ".obj")
                        type = "3D_OTHER";
                    else
                        type = "OTHER";

                    totalCount++;

                    // Escape CSV fields that might contain commas
                    string csvRow = string.Join(",",
                        Escape(type),
                        Escape(key),
                        Escape(folder),
                        Escape(filename),
                        Escape(ext),
                        sizeBytes.ToString(),
                        Escape(FormatSize(sizeBytes)),
                        Escape(modified)
                    );

                    rows.Add(csvRow);
                }

                request.ContinuationToken = response.NextContinuationToken;

            } while (response.IsTruncated == true);

            // Write to persistent data path so it survives Play mode
            string outputPath = Path.Combine(
                Application.persistentDataPath,
                $"S3_Bucket_Contents_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            );

            await File.WriteAllLinesAsync(outputPath, rows);

            Debug.Log("─────────────────────────────────────────");
            Debug.Log($"[S3BucketInspector] Export complete.");
            Debug.Log($"  Total objects : {totalCount}");
            Debug.Log($"  GLB models    : {glbCount}");
            Debug.Log($"  Saved to      : {outputPath}");
            Debug.Log("─────────────────────────────────────────");
        }
        catch (AmazonS3Exception e)
        {
            Debug.LogError($"[S3BucketInspector] S3 error: {e.Message}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[S3BucketInspector] Error: {e.Message}");
        }
    }

    private string Escape(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private string FormatSize(long bytes)
    {
        if (bytes == 0)          return "0 B";
        if (bytes < 1024)        return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024f:F1} KB";
        return $"{bytes / (1024f * 1024f):F1} MB";
    }
}