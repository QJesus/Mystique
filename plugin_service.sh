#!/bin/bash

mode=$1
program=$2
version=$3
source=$4
target=$5

service_name=$program.$version.service

cache=$target/caches
if [ ! -d $cache ]; then
    echo "mkdir -p $cache"
    mkdir -p $cache
fi

if [ $mode == "enable" ]; then
    systemctl unmask $service_name
    systemctl enable $service_name
    systemctl start $service_name
    systemctl daemon-reload
elif [ $mode == "disable" ]; then
    systemctl kill $service_name
    systemctl disable $service_name
    systemctl mask $service_name
    systemctl daemon-reload
elif [ $mode == "remove" ]; then
    systemctl kill $service_name
    systemctl disable $service_name
    systemctl daemon-reload
    rm /etc/systemd/system/$service_name
elif [ $mode == "add" ]; then

    echo "generate a listen port for current site:"
    random_port=true
    listen_port={port}
    if [ $random_port == "true" ]; then
        ip=127.0.0.1
        first_port=29175
        last_port=59172
        for ((port = $first_port; port <= $last_port; port++)); do
            (echo >/dev/tcp/$ip/$port) >/dev/null 2>&1 && continue || break
        done
        listen_port=$port
    fi
    echo $listen_port >$cache/port_$program
    echo "listen_port=$listen_port"

    echo "kill old site service: $service_name"
    systemctl daemon-reload
    systemctl kill $service_name
    systemctl disable $service_name

    echo "update site. $source to $target"
    echo "[Unit]
Description=$program

[Service]
WorkingDirectory=$target
ExecStart=$target/$program --urls=http://127.0.0.1:$listen_port
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
    mv $source $target
    echo "chmod +x $target/$program"
    chmod +x $target/$program
else
    echo "invalid $mode. enable, disable, remove, add"
fi

#systemctl status $service_name -l
#
#sleep 1.2
#
#netstat -tlpn
#