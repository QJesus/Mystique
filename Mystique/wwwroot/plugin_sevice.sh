#!/bin/bash

mode=$1
program=$2
version=$3
folder=$4

service_name=$program.$version.service

if [ $mode == "enable" ]; then
    systemctl unmask $service_name
    systemctl enable $service_name
    systemctl start $service_name
    systemctl daemon-reload
elif [ $mode == "disable" ]; then
    systemctl stop $service_name
    systemctl disable $service_name
    systemctl mask $service_name
    systemctl daemon-reload
elif [ $mode == "remove" ]; then
    systemctl stop $service_name
    systemctl disable $service_name
    systemctl daemon-reload
    rm /etc/systemd/system/$service_name
elif [ $mode == "add" ]; then

    listen_port={port}
    # source=$(cd `dirname $0`; pwd)/$folder
    source=$(cd `dirname $0`; pwd)/$folder
    target=/opt/smt/eusb_terminal
    random_port=true

    if [ $random_port == "true" ]; then
        ip=127.0.0.1
        first_port=29175
        last_port=59172
        for ((port = $first_port; port <= $last_port; port++)); do
            (echo >/dev/tcp/$ip/$port) >/dev/null 2>&1 && continue || break
        done
        echo $port=-
        listen_port=$port
    fi

    if [ ! -d $target ]; then
        echo "mkdir -p $target"
        mkdir -p $target
    fi

    if [ ! -d $source ]; then
        echo "error. $source not found"
    else
        echo "stop $service_name"
        systemctl daemon-reload
        systemctl stop $service_name
        systemctl disable $service_name

        echo "[Unit]
Description=$program

[Service]
WorkingDirectory=$target/$folder
ExecStart=/home/pi/dotnet/dotnet $target/$folder/$program.dll --urls=http://127.0.0.1:$listen_port
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

        echo "cp -r $source $target"
        cp -r $source $target

        echo "start $program"
        systemctl enable $service_name
        systemctl start $service_name
    fi

    if [ ! -d $target/pids ]; then
        echo "mkdir -p $target/pids"
        mkdir -p $target/pids
    fi

    echo $listen_port >$target/pids/$program

else
    echo "invalid $mode. enable, disable, remove, add"
fi

sleep 1

systemctl status $service_name -l

netstat -tlpn

# example: 
# sudo ./y.sh add Miao.Web 20191204 miao.web.self-contained
# sudo ./y.sh disable Miao.Web 20191204
# sudo ./y.sh enable Miao.Web 20191204
# sudo ./y.sh remove Miao.Web 20191204
