# azureauth-credprovider-keyring

Thin Python keyring backend and controlled keyring CLI shim for
`azureauth-credprovider`.

This package invokes the installed product helper by its configured absolute
path using the fixed `keyring-helper-v2` command. It does not acquire credentials
directly and does not write credential material to disk.
