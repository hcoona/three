# azureauth-credprovider-keyring

Thin Python keyring backend and controlled keyring CLI shim for
`azureauth-credprovider`.

This package validates a product-owned helper integrity manifest before invoking
the fixed `keyring-helper-v2` command. It does not acquire credentials directly
and does not write credential material to disk.
