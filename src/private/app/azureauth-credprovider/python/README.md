# azureauth-credprovider-keyring

Thin Python keyring backend and controlled keyring CLI shim for
`azureauth-credprovider`.

This package invokes the installed product helper by its configured absolute
path using the fixed `keyring-helper-v2` command. It does not acquire credentials
directly and does not write credential material to disk.

`azureauth-credprovider configure python` writes a backend manifest with the
protocol major, product ID, current platform, and absolute path to the installed
`azureauth-credprovider` apphost. Backend calls use this non-shell argv shape:

```text
<absolute-product-apphost> python-keyring get ...
```

The wheel provides the `azureauth-keyring` console script for direct package
testing and import-mode environments. Product configuration creates a
controlled-PATH POSIX `keyring` shim that delegates uv and pip subprocess calls
directly to the installed product apphost, avoiding a dependency on the project
environment being synchronized first. Windows import mode is supported by the
backend manifest, while Windows subprocess mode remains deferred until a real
`keyring.exe` launcher is implemented. Installing the deployment bundle copies
this wheel into the product installation but does not install it into a Python
environment automatically.
