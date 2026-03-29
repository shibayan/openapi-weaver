using SampleApp;

var client = new ApiSchemaClient(accessToken: "dummy-access-token");

var list = await client.Companies.GetAsync();

Console.WriteLine(list.Companies[0].GetType());

Console.WriteLine(client.Tags.GetType().FullName);
