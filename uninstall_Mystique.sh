#!/bin/bash

folder=Mystique.arm64
program=Mystique
service_name=$program.service
target=/opt/smt/eusb_terminal

systemctl kill $service_name
systemctl disable $service_name
systemctl daemon-reload
systemctl status $service_name -l

echo "rm /etc/systemd/system/$service_name"
rm /etc/systemd/system/$service_name

echo "rm -rf $target/$folder"
rm -rf $target/$folder

