#!/bin/bash

folder=Mystique.arm64
program=Mystique
listen_port=12580

service_name=$program.service

# source=$(cd `dirname $0`; pwd)/$folder
source=$(cd `dirname $0`; pwd)/$folder
if [ ! -d $source ]; then
    echo "error(404). $source not found"
    return
fi

target=/opt/smt/eusb_terminal/$folder
if [ ! -d $target ]; then
    echo "mkdir -p $target"
    mkdir -p $target
fi

echo "kill old $service_name"
systemctl daemon-reload
systemctl kill $service_name
systemctl disable $service_name

echo "[Unit]
Description=$program

[Service]
WorkingDirectory=$target
ExecStart=$target/$program --urls=http://*:$listen_port
Restart=always
RestartSec=12
KillSignal=SIGINT
StandardOutput=syslog
StandardError=syslog
SyslogIdentifier=$program
User=root
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target" >/etc/systemd/system/$service_name

echo "cp -r $source /opt/smt/eusb_terminal"
cp -r $source /opt/smt/eusb_terminal
echo "chmod +x $target/$program"
chmod +x $target/$program
echo "chmod +x $target/plugin_service.sh"
chmod +x $target/plugin_service.sh

echo "start $service_name"
systemctl enable $service_name
systemctl start $service_name

systemctl status $service_name -l
