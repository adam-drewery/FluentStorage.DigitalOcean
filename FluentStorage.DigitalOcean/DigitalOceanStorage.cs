using Amazon.S3;
using Amazon.S3.Model;
using FluentStorage.Blobs;

namespace FluentStorage.DigitalOcean;

public class DigitalOceanStorage : IBlobStorage
{
    private readonly AmazonS3Client _s3Client;
    private readonly string _bucketName;
    private readonly S3CannedACL _acl;

    public DigitalOceanStorage(string accessKey, string secretKey, string bucketName, S3CannedACL acl)
    {
        var config = new AmazonS3Config
        {
            ServiceURL = "https://ams3.digitaloceanspaces.com", 
        };

        _s3Client = new AmazonS3Client(accessKey, secretKey, config);
        _bucketName = bucketName;
        _acl = acl;
    }

    public async Task<IReadOnlyCollection<Blob>> ListAsync(ListOptions? options = null, CancellationToken cancellationToken = default)
    {
        var listResponse = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = _bucketName }, cancellationToken);
        return listResponse.S3Objects.Select(o => new Blob(o.Key)).ToList();
    }

    public async Task WriteAsync(string fullPath, Stream dataStream, bool append = false, CancellationToken cancellationToken = default)
    {
        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = fullPath,
            InputStream = dataStream,
            CannedACL = _acl
        }, cancellationToken);
    }

    public async Task<Stream> OpenReadAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        var getObjectResponse = await _s3Client.GetObjectAsync(_bucketName, fullPath, cancellationToken);
        return getObjectResponse.ResponseStream;
    }

    public async Task DeleteAsync(IEnumerable<string> fullPaths, CancellationToken cancellationToken = default)
    {
        foreach (var path in fullPaths)
        {
            await _s3Client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = _bucketName,
                Key = path
            }, cancellationToken);
        }
    }

    public async Task<IReadOnlyCollection<bool>> ExistsAsync(IEnumerable<string> fullPaths, CancellationToken cancellationToken = default)
    {
        var existsTasks = fullPaths.Select(async path =>
        {
            try
            {
                await _s3Client.GetObjectMetadataAsync(_bucketName, path, cancellationToken);
                return true;
            }
            catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }
        }).ToList();

        return await Task.WhenAll(existsTasks);
    }

    public Task<IReadOnlyCollection<Blob>> GetBlobsAsync(IEnumerable<string> fullPaths, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyCollection<Blob>>(fullPaths.Select(path => new Blob(path)).ToList());
    }

    public async Task SetBlobsAsync(IEnumerable<Blob> blobs, CancellationToken cancellationToken = default)
    {
        // For now, just setting metadata. Expand as needed.
        foreach (var blob in blobs)
        {
            var request = new CopyObjectRequest
            {
                SourceBucket = _bucketName,
                SourceKey = blob.FullPath,
                DestinationBucket = _bucketName,
                DestinationKey = blob.FullPath,
                MetadataDirective = S3MetadataDirective.REPLACE,
                CannedACL = _acl
            };

            await _s3Client.CopyObjectAsync(request, cancellationToken);
        }
    }

    public Task<ITransaction?> OpenTransactionAsync()
    {
        // Transactions not supported here, return null
        return Task.FromResult<ITransaction?>(null);
    }

    public void Dispose()
    {
        _s3Client.Dispose();
    }
}