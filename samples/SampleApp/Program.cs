using SampleApp;

var client = new ApiSchemaClient(accessToken: "dummy-access-token");

var list = await client.Companies.GetAsync();

Console.WriteLine(client.Tags.GetType().FullName);
