using SampleApp;

var client = new ApiSchemaClient(accessToken: "dummy-access-token");

var list = await client.Companies.ListAsync();

Console.WriteLine(client.Tags.GetType().FullName);
