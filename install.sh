#!/bin/bash

if [ ! -d "/opt/smt/eusb_terminal/" ]; then
    mkdir -p /opt/smt/eusb_terminal/
    echo "mkdir /opt/smt/eusb_terminal/"
fi

if [ ! -d "$(cd `dirname $0`; pwd)/Mystique" ]; then
    echo "error. $(cd `dirname $0`; pwd)/Mystique not found"
else
    firewall-cmd --zone=public --remove-port=12580/tcp --permanent
    systemctl daemon-reload
    systemctl stop Mystique.service
    systemctl disable Mystique.service

    echo "[Unit]
Description=Mystique

[Service]
WorkingDirectory=/opt/smt/eusb_terminal/Mystique
ExecStart=/opt/smt/eusb_terminal/Mystique/Mystique --urls=http://*:12580
Restart=always
RestartSec=12
KillSignal=SIGINT
StandardOutput=syslog
StandardError=syslog
SyslogIdentifier=Mystique
User=root
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target" >/etc/systemd/system/Mystique.service
 
    cp -r $(cd `dirname $0`; pwd)/Mystique /opt/smt/eusb_terminal/
    echo "cp -r /opt/smt/eusb_terminal/Mystique"

    # systemctl restart rsyslog
    systemctl enable Mystique.service
    systemctl start Mystique.service
fi

firewall-cmd --zone=public --add-port=12580/tcp --permanent
firewall-cmd --reload
echo "export host port 12580"

systemctl status Mystique.service -l
