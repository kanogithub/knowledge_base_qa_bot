using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.Configuration;

namespace CloudKB.Infrastructure;

public class S3StorageService : IStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;

    public S3StorageService(IAmazonS3 s3Client, IConfiguration config)
    {
        _s3Client = s3Client;
        _bucketName = config["Storage:BucketName"] ?? "knowledge-base";
        
        // Ensure bucket exists on startup
        EnsureBucketExistsAsync().GetAwaiter().GetResult();
    }

    private async Task EnsureBucketExistsAsync()
    {
        try
        {
            var exists = await AmazonS3Util.DoesS3BucketExistV2Async(_s3Client, _bucketName);
            if (!exists)
            {
                var putRequest = new PutBucketRequest
                {
                    BucketName = _bucketName
                };
                await _s3Client.PutBucketAsync(putRequest);
                Console.WriteLine($"Created S3 bucket: {_bucketName}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error ensuring bucket exists: {ex.Message}");
        }
    }

    public async Task UploadAsync(string tenantId, string fileName, Stream fileStream, CancellationToken ct)
    {
        var key = $"{tenantId}/raw/{fileName}";
        var putRequest = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = fileStream,
            AutoCloseStream = false // Keep form stream alive
        };

        await _s3Client.PutObjectAsync(putRequest, ct);
    }

    public async Task<string> DownloadAsync(string tenantId, string fileName, CancellationToken ct)
    {
        var key = $"{tenantId}/raw/{fileName}";
        using var response = await _s3Client.GetObjectAsync(_bucketName, key, ct);
        using var reader = new StreamReader(response.ResponseStream);
        return await reader.ReadToEndAsync(ct);
    }

    public async Task DeleteAsync(string tenantId, string fileName, CancellationToken ct)
    {
        var key = $"{tenantId}/raw/{fileName}";
        try
        {
            await _s3Client.DeleteObjectAsync(_bucketName, key, ct);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Ignore if object not found in S3
        }
    }
}
