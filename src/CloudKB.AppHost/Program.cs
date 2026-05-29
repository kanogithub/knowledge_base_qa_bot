var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure Containers
var postgresPassword = builder.AddParameter("postgresql-password", secret: true);
var rabbitmqPassword = builder.AddParameter("rabbitmq-password", secret: true);

var postgres = builder.AddPostgres("postgres", password: postgresPassword)
                      .WithDataVolume();
var db = postgres.AddDatabase("cloudkb");
var redis = builder.AddRedis("redis")
                   .WithDataVolume();
var rabbitmq = builder.AddRabbitMQ("rabbitmq", password: rabbitmqPassword)
                      .WithDataVolume();

var minio = builder.AddContainer("minio", "minio/minio")
                    .WithArgs("server", "/data", "--console-address", ":9001")
                    .WithVolume("minio-data", "/data")
                    .WithHttpEndpoint(port: 9000, targetPort: 9000, name: "s3")
                    .WithHttpEndpoint(port: 9001, targetPort: 9001, name: "console");

var localStoragePath = Path.Combine(builder.AppHostDirectory, "..", "LocalStorage");

var indexingApi = builder.AddProject<Projects.CloudKB_ApiService_Indexing>("apiservice-indexing")
                         .WithEnvironment("ConnectionStrings__minio", minio.GetEndpoint("s3"))
                         .WithEnvironment("Storage__LocalPath", localStoragePath)
                         .WithReference(rabbitmq)
                         .WithReference(db)
                         .WaitFor(rabbitmq)
                         .WaitFor(db);

var notificationApi = builder.AddProject<Projects.CloudKB_ApiService_Notification>("apiservice-notification")
                             .WithReference(redis)
                             .WithReference(db)
                             .WaitFor(redis)
                             .WaitFor(db);

var chatApi = builder.AddProject<Projects.CloudKB_ApiService_Chat>("apiservice-chat")
                     .WithReference(db)
                     .WithReference(redis)
                     .WaitFor(db)
                     .WaitFor(redis);

var worker = builder.AddProject<Projects.CloudKB_Worker_Indexer>("worker-indexer")
                    .WithEnvironment("ConnectionStrings__minio", minio.GetEndpoint("s3"))
                    .WithEnvironment("Storage__LocalPath", localStoragePath)
                    .WithReference(rabbitmq)
                    .WithReference(db)
                    .WithReference(redis)
                    .WaitFor(rabbitmq)
                    .WaitFor(db)
                    .WaitFor(redis);

var gateway = builder.AddProject<Projects.CloudKB_Gateway>("gateway")
                     .WithReference(indexingApi)
                     .WithReference(notificationApi)
                     .WithReference(chatApi)
                     .WaitFor(indexingApi)
                     .WaitFor(notificationApi)
                     .WaitFor(chatApi);


builder.Build().Run();
