#!/bin/bash

program=Mystique
service_name=$program.service
target=/opt/smt/eusb_terminal

systemctl stop $service_name
systemctl disable $service_name
systemctl daemon-reload

rm /etc/systemd/system/$service_name

rm -rf $target/$folder

systemctl status $service_name -l

sleep 1.2

netstat -tlpn
