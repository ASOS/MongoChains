What is MongoChains?
--------------------

MongoChains is a database migration tool for MongoDB that uses plain MongoDB javascript to transition a database from one version to another.

There is a .NET Standard Library (MongoChains), and a Console Runner (MongoChains.Console). The former is useful if you want to call MongoChains programatically but most users will probably just want to grab the console runner. MongoChains.Console is a .NET Core application so you can compile it to a native executable for your architecture if necessary.

It can also be integrated easily with build tools such as FAKE, Octopus Deploy, Teamcity etc.


MongoChains.Console Usage
-------------------------

    USAGE: mongochains [--help] --target <connectionString> --path <path> [--targetversion <version>] [--token <key>=<value>] [--safemode]

    OPTIONS:

        --target <connectionString>
                            connection string for the mongo database
        --path <path>         path to migrations files
        --targetversion <version>
                            target a specific version
        --token <key>=<value> a token to be replaced in the migrations
        --safemode            abort without running scripts if current version of db cannot be determined
        --help                display this list of options.

Getting started
---------------
Migrations files are just javascript mongo scripts. You should put the migrations into a sequence of numbered directories. Each migration should be called ```up.js```. For example:

    migrations
    ├───1
    │       up.js
    │
    ├───2
    │       up.js
    │
    └───3
            up.js

If you have .NET Core 2.1 installed, you can install mongochains as a global tool:

    dotnet tool install --global MongoChains.Console

Then run it as follows:

    mongochains --target "mongodb://localhost:27017" --path "/path/to/migrations"

Migrations may also contain tokens which get replaced before they are applied. This is useful when you have, for example, settings that vary between your development and production environments.

Anything in the javascript files of the form ```{#Key}``` is treated as a case-sensitive token. Before attempting to apply a migration, mongochains will ensure that you have specified values for all the tokens in that file. e.g.

    mongochains --target "mongodb://localhost:27017" --path "/path/to/migrations" --token foo=bar --token buzz=bazz

By default, MongoChains will check the current version of the database by looking at the migrations collection in the ```admin``` DB, and will apply in sequence all migrations it finds that are above the current version. You can use the ```targetversion``` parameter to specify a particular version to migrate to. MongoChains migrations only go upwards, so this essentially just ignores migrations above the version you specify.

Finally, there is a ```safemode``` argument that you can specify which causes MongoChains to abort early if it cannot determine the current version of the database (that is, there is no record in the migrations collection). This is just an extra sanity check to provide reassurance in production environments.
