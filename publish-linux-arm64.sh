dotnet publish -o Mystique.arm64 -r linux-arm64 -c Release --self-contained

zip -r Mystique-linux-arm64.zip Mystique-linux-arm64/ install_Mystique.sh uninstall_Mystique.sh

