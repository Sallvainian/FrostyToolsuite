#!/bin/sh
# Starts a Frosty binary from the package's lib directory.
# .NET needs ICU for culture data; fall back to invariant mode instead of crashing on systems without it.
if [ -z "$DOTNET_SYSTEM_GLOBALIZATION_INVARIANT" ] && ! ls /usr/lib/libicuuc.so.* /usr/lib64/libicuuc.so.* \
    /usr/lib/x86_64-linux-gnu/libicuuc.so.* /lib/x86_64-linux-gnu/libicuuc.so.* >/dev/null 2>&1; then
    export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
fi
exec /usr/lib/frosty-mod-manager/@BINARY@ "$@"
