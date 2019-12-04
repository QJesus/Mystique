#!/bin/bash

program=Mystique
listen_port=12580
# source=$(cd `dirname $0`; pwd)/$program
source=$(cd `dirname $0`; pwd)/$program
target=/opt/smt/eusb_terminal
export_firewall=true
random_port=false
ip=127.0.0.1

if [ $export_firewall == "true" ]; then
    ip=*
fi

if [ $random_port == "true" ]; then
    ip=127.0.0.1
    first_port=29175
    last_port=59172
    for ((port = $first_port; port <= $last_port; port++)); do
        (echo >/dev/tcp/$ip/$port) >/dev/null 2>&1 && continue || break
    done
    echo $port
    listen_port=$port
fi

if [ ! -d $target ]; then
    echo "mkdir -p $target"
    mkdir -p $target
fi

if [ ! -d $source ]; then
    echo "error. $source not found"
else
    echo "--remove-port=$listen_port/tcp"
    firewall-cmd --zone=public --remove-port=$listen_port/tcp --permanent

    echo "stop $program"
    systemctl daemon-reload
    systemctl stop $program.service
    systemctl disable $program.service

    echo "[Unit]
Description=$program

[Service]
WorkingDirectory=$target/$program
ExecStart=$target/$program/$program --urls=http://$ip:$listen_port
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
WantedBy=multi-user.target" >/etc/systemd/system/$program.service

    echo "cp -r $source $target"
    cp -r $source $target
    chmod +x $target/$program/$program

    echo "start $program"
    # systemctl restart rsyslog
    systemctl enable $program.service
    systemctl start $program.service
    if [ $export_firewall == "true" ]; then
        echo "--add-port=$listen_port/tcp"
        firewall-cmd --zone=public --add-port=$listen_port/tcp --permanent
    fi
    echo "reload firewall"
    firewall-cmd --reload
fi

systemctl status $program.service -l
