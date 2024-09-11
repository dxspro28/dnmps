#!/bin/bash

# Check for root access
if [ "$EUID" -ne 0 ]; then
    echo "You must run this script as root."
    exit 1
fi

echo "Installing dependencies"
    
cp ./lib/libbass.so /usr/lib/

if command -v mono &> /dev/null
then
    echo "Mono runtime is already installed"
else
    pacman -S mono --needed --noconfirm
fi

make build

cp ./bin/dnmps /usr/bin/
chmod +x /usr/bin/dnmps

echo "Installation finished!"
