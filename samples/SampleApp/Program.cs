using SampleApp;

var client = new ApiSchemaClient(accessToken: "dummy-access-token");

Console.WriteLine(client.Tags.GetType().FullName);
