#!/bin/sh

TARGET_DIR="/data/logFiles/NIM"
SIZE_LIMIT=100
dir_size=$(du -sm "$TARGET_DIR" | awk '{print $1}')
echo "[$(date +'%F %T')] : $TARGET_DIR : ${dir_size}MB" >> "$log"
if [ "$dir_size" -gt "$SIZE_LIMIT" ]; then
        rm $TARGET_DIR/* -r
	new_size=$(du -sm "$TARGET_DIR" | awk '{print $1}')
        echo "[$(date +'%F %T')] : ${new_size}MB" >> "$log"
fi
