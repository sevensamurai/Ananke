using Ananke.Tool.Platform.Azure;

// This executable's sole purpose is to install the adapter DLL into
// ~/.nnke-platform/adapters/ so that nnke-platform can load it on next run.
AdapterInstaller.Run(args);
