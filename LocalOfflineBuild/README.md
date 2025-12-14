# Local Offline Build

`build-offline.ps1` builds the loader, module, and plugin installer so you can test Safeturned locally without CI or the API.

Before running it, make sure the loader config you deploy next to `/Modules/Safeturned.Loader` contains:
```json
{
  "EnableCustomInstaller": true,
  "CustomInstallerPath": "SafeturnedBuild/Safeturned.PluginInstaller",
  "CustomPluginPath": "SafeturnedBuild/Safeturned.Module"
}
```

That is the config file you edit on your test server; you only need to update it once unless you want different paths.

If that's an external server where you will test it, you might want to do something like this. Example Unturned test layout:

```
/
├── Modules/
│   └── Safeturned.Loader/
│       └── SafeturnedBuild/
│           └── Safeturned.Module/
├── SafeturnedBuild/
├── Extras/
└── Servers/
    └── MyServer/
```
