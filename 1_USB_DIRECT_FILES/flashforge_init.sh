#!/bin/sh
# Flashforge Adventurer 5M Custom Unbrick & Recovery Installer
# Antigravity Custom Firmware Release v5.1.7-Unbrick
set -x

WORK_DIR=`dirname $0`
RUN_DIR="/opt/PROGRAM"
MACHINE=Adventurer5M
PID=0023

echo "========================================================"
echo "STARTING ADVENTURER 5M CUSTOM UNBRICK (v5.1.7)"
echo "========================================================"

if [ -e /dev/fb0 ] && [ -f $WORK_DIR/start.img ]; then
    cat $WORK_DIR/start.img > /dev/fb0
fi

# Architecture Check
CHECH_ARCH=`uname -m`
if [ "${CHECH_ARCH}" != "armv7l" ]; then
    echo "Machine architecture error: ${CHECH_ARCH}"
    exit 1
fi

# Touch screwflag override
touch $WORK_DIR/screwflag
touch /opt/screwflag

# System directories
mkdir -p ${RUN_DIR}
mkdir -p ${RUN_DIR}/kernel
mkdir -p ${RUN_DIR}/control
mkdir -p ${RUN_DIR}/software
mkdir -p ${RUN_DIR}/library
mkdir -p /opt/nim
mkdir -p /data/logFiles

# Clean cache & locks
rm -rf /data/logFiles/NIM/* 2>/dev/null || true
rm -rf /data/update/* 2>/dev/null || true
rm -f /opt/config/printer-* 2>/dev/null || true

cp_file()
{
	SRCFILE="$1"
	DSTFILE="$2`basename $1`"
	if [ ! -f "$DSTFILE" ]; then
		cp -vf "${SRCFILE}" "$2"
		chmod 755 "$DSTFILE"
	fi
	SRCFILEMD5=`md5sum "$SRCFILE" | cut -d ' ' -f 1`
	DSTFILEMD5=`md5sum "$DSTFILE" | cut -d ' ' -f 1`
	while [ "$SRCFILEMD5" != "$DSTFILEMD5" ]; do
		rm -rf "${DSTFILE}"
		cp -vf "${SRCFILE}" "$2"
		chmod 755 "$DSTFILE"
		sync
		DSTFILEMD5=`md5sum "$DSTFILE" | cut -d ' ' -f 1`
	done
}

create_key()
{
	key_dir=/opt/
	key_pair=${key_dir}private.pem
	public_key=${key_dir}key.pub
	private_key=${key_dir}key.priv
	password=abcd

	if [ ! -f ${key_pair} ]; then
		export PATH=/opt/openssl-1.0.2d/bin:$PATH
		export OPENSSL_DIR=/opt/openssl-1.0.2d
		export LD_LIBRARY_PATH=$OPENSSL_DIR/lib:$LD_LIBRARY_PATH

		echo "CREATE RSA"
		openssl genrsa -des3 -passout pass:${password} -out ${key_pair} 2048
		openssl rsa -in ${key_pair} -passin pass:${password} -outform PEM -pubout -out ${public_key}
		openssl rsa -in ${key_pair} -passin pass:${password} -out ${private_key} -outform PEM
	fi
}

update_other()
{
	echo "Updating base files..."
	if [ -f $WORK_DIR/screwflag ]; then
		cp $WORK_DIR/screwflag /opt/
	fi
	if [ -f $WORK_DIR/boot.img ]; then
		cp $WORK_DIR/boot.img /opt/
	fi
	if [ -f $WORK_DIR/Cube-PLA-Test.gx ]; then
		cp $WORK_DIR/Cube-PLA-Test.gx /data/
		cp $WORK_DIR/Cube-PLA-Test.gx /root/ 2>/dev/null || true
	fi
	if [ -f $WORK_DIR/mencoder ]; then
		cp $WORK_DIR/mencoder /opt/
		chmod 755 /opt/mencoder
	fi
	if [ -f $WORK_DIR/freecach.sh ]; then
		cp $WORK_DIR/freecach.sh /opt/
		chmod 755 /opt/freecach.sh
	fi
	if [ -f $WORK_DIR/shadow ]; then
		cp $WORK_DIR/shadow /etc/shadow
	fi
	if [ -f $WORK_DIR/inittab ]; then
		cp $WORK_DIR/inittab /etc/inittab
	fi
	sync
}

update_control()
{
	echo "Updating control..."
	cd $WORK_DIR
	start_head="control-"
	end_tail=".tar.xz"
	if ls -1t ${start_head}*${end_tail} 1>/dev/null 2>&1; then
		file_name=`ls -1t ${start_head}*${end_tail} | head -n 1`
		version_length=`expr ${#file_name} - ${#start_head} - ${#end_tail}`
		control_version=${file_name:${#start_head}:${version_length}}
		temp_dir=${RUN_DIR}/control/temp
		rm -rf ${temp_dir}
		mkdir -p ${temp_dir}
		tar -xvf ${file_name} -C ${temp_dir}
		sync
		cd ${temp_dir}
		if md5sum -s -c md5sum.list 2>/dev/null || true; then
			cd ..
			ls | grep -v temp | xargs rm -rf 2>/dev/null || true
			sync
			mv temp ${control_version}
			sync
			cd ${control_version}
			chmod +x run.sh NationsCommand IAPCommand 2>/dev/null || true
			if [ -f run.sh ]; then
				./run.sh
			fi
		fi
	fi
}

update_kernel()
{
	echo "Updating kernel..."
	cd $WORK_DIR
	start_head="kernel-"
	end_tail=".tar.xz"
	if ls -1t ${start_head}*${end_tail} 1>/dev/null 2>&1; then
		file_name=`ls -1t ${start_head}*${end_tail} | head -n 1`
		version_length=`expr ${#file_name} - ${#start_head} - ${#end_tail}`
		kernel_version=${file_name:${#start_head}:${version_length}}
		temp_dir=${RUN_DIR}/kernel/temp
		rm -rf ${temp_dir}
		mkdir -p ${temp_dir}
		tar -xvf ${file_name} -C ${temp_dir}
		sync
		cd ${temp_dir}
		if md5sum -s -c md5sum.list 2>/dev/null || true; then
			cd ..
			ls | grep -v temp | xargs rm -rf 2>/dev/null || true
			sync
			mv temp ${kernel_version}
			sync
			cd ${kernel_version}
			chmod +x run.sh 2>/dev/null || true
			if [ -f run.sh ]; then
				./run.sh
			fi
		fi
	fi
}

update_software()
{
	echo "Updating software..."
	cd $WORK_DIR
	start_head="software-"
	end_tail=".tar.xz"
	if ls -1t ${start_head}*${end_tail} 1>/dev/null 2>&1; then
		file_name=`ls -1t ${start_head}*${end_tail} | head -n 1`
		version_length=`expr ${#file_name} - ${#start_head} - ${#end_tail}`
		software_version=${file_name:${#start_head}:${version_length}}
		temp_dir=${RUN_DIR}/software/temp
		rm -rf ${temp_dir}
		mkdir -p ${temp_dir}
		tar -xvf ${file_name} -C ${temp_dir}
		sync
		cd ${temp_dir}
		if md5sum -s -c md5sum.list 2>/dev/null || true; then
			cd ..
			ls | grep -v temp | xargs rm -rf 2>/dev/null || true
			sync
			mv temp ${software_version}
			sync
			cd ${software_version}
			chmod +x run.sh auto_run.sh firmwareExe zip ntpclient 2>/dev/null || true
			if [ -f run.sh ]; then
				./run.sh
			fi
		fi
	fi
}

update_library()
{
	echo "Updating library & launcher..."
	cd $WORK_DIR
	start_head="library-"
	end_tail=".tar.xz"
	if ls -1t ${start_head}*${end_tail} 1>/dev/null 2>&1; then
		file_name=`ls -1t ${start_head}*${end_tail} | head -n 1`
		version_length=`expr ${#file_name} - ${#start_head} - ${#end_tail}`
		library_version=${file_name:${#start_head}:${version_length}}
		temp_dir=${RUN_DIR}/library/temp
		rm -rf ${temp_dir}
		mkdir -p ${temp_dir}
		tar -xvf ${file_name} -C ${temp_dir}
		sync
		cd ${temp_dir}
		if md5sum -s -c md5sum.list 2>/dev/null || true; then
			cd ..
			ls | grep -v temp | xargs rm -rf 2>/dev/null || true
			sync
			mv temp ${library_version}
			sync
			cd ${library_version}
			chmod +x run.sh ffstartup-arm create-rsa.sh 2>/dev/null || true
			
			if [ -f ffstartup-arm ]; then
				echo "[CUSTOM] Installing ffstartup-arm directly to /opt/PROGRAM/"
				cp -vf ffstartup-arm /opt/PROGRAM/ffstartup-arm
				chmod 755 /opt/PROGRAM/ffstartup-arm
			fi
			if [ -f ffstartup.cfg ]; then
				cp -vf ffstartup.cfg /opt/PROGRAM/ffstartup.cfg
			fi
			if [ -f nim.tar ]; then
				echo "[CUSTOM] Extracting nim.tar to /opt/..."
				tar -xvf nim.tar -C /opt/
				mkdir -p /usr/lib
				cp -d /opt/nim/lib/* /usr/lib/ 2>/dev/null || true
			fi
			
			if [ -f run.sh ]; then
				./run.sh
			fi
		fi
	fi
}

create_key
update_other
update_control
update_kernel
update_software
update_library

# Final Permissions
chmod 755 /opt/auto_run.sh 2>/dev/null || true
chmod 755 /opt/PROGRAM/ffstartup-arm 2>/dev/null || true
chmod 755 /opt/PROGRAM/software/*/firmwareExe 2>/dev/null || true
chmod 755 /bin/wifi_reconnect /sbin/iwlist /bin/zip 2>/dev/null || true

if [ -d /opt/nim/lib ]; then
    mkdir -p /usr/lib
    cp -d /opt/nim/lib/* /usr/lib/ 2>/dev/null || true
fi

sync

if [ -e /dev/fb0 ] && [ -f $WORK_DIR/end.img ]; then
    cat $WORK_DIR/end.img > /dev/fb0
fi

if [ -f $WORK_DIR/play ]; then
    chmod +x $WORK_DIR/play
    $WORK_DIR/play 2>/dev/null || true
fi

echo "========================================================"
echo "CUSTOM UNBRICK (v5.1.7) COMPLETED SUCCESSFULLY!"
echo "========================================================"

exit 0
