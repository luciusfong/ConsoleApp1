# ==============================================================
# DeviceScanner machine profile
# EXAMPLE built from the SD1X block in FactoryReset - edit it to match the real machine.
# To test another machine, copy this file and change the values. Nothing else needs editing.
#
# Format:  key = value        '#' starts a comment
# Repeatable keys: home.programs, ip.expect, com.expect, io
# ==============================================================

name = I10-TWIN-SHARED

# ---------- controller ----------
zmc.ip        = 192.168.0.11
zmc.init      = INITSYSTEM
home.programs = GHOME           # started together, in this order
home.axes     = 6, 7, 8, 9, 10, 11, 12, 13       # axes to wait on; leave empty to auto-discover

# ---------- devices that must be present ----------
# ip.expect  = <ip>   | <device>    device: CL3 (Keyence) | OPT light | Camera GigE | ZMC
# com.expect = <port> | <device>    device: ITV regulator | MT balance | OPT light | LC18 light
#                                           Temp TK | Temp TX | Temp TM2 | Temp TM4
# The ZMC at zmc.ip is checked automatically.
com.slaves = 1, 2, 3, 4                  # temperature controller slave addresses to try

ip.expect  = 192.168.0.101 | Camera GigE
ip.expect  = 192.168.0.102 | Camera GigE
ip.expect  = 192.168.0.20 | OPT light
ip.expect  = 192.168.0.30 | OPT light
ip.expect  = 192.168.1.10  | CL3 (Keyence)
ip.expect  = 192.168.1.17  | CL3 (Keyence)

com.expect = COM3 | ITV regulator
com.expect = COM4 | ITV regulator
com.expect = COM5 | ITV regulator
com.expect = COM6 | ITV regulator
com.expect = COM7 | ViscoTec EcoSpray
com.expect = COM10 | ViscoTec EcoSpray
# com.expect = COM3 | MT balance         # weigh is StartUpEnable=false in FactoryReset

# ---------- discrete IO loopback ----------
# io = <name> | <output> | <expected input> | <settle ms> | optional
#   expected input blank or '-' : do not check a pair, just report which inputs react
#   settle ms blank             : use IoSettleMs from ScanConfig
#   optional                    : a failure is shown as WARN and does not fail the run
# Outputs really switch while this runs.

# I/O map: I_SERIES_TWIN / TWIN_DL / TWIN_SHARED_STATION (ELabelDO / ELabelDI)
# Each valve output is paired with the sensor that should follow it. Up runs before down, so a lifter
# left up by a double-acting valve is brought back down by the next test.

# process station, lane 1
io = PRO A1 stopper up        | 36 | 38 | 1000
io = PRO A1 lifter up         | 37 | 39 | 1000
io = PRO 1 lifter down        | 38 | 40 | 1000
# process station, lane 2
io = PRO A2 stopper up        | 39 | 44 | 1000
io = PRO A2 lifter up         | 40 | 45 | 1000
io = PRO 2 lifter down        | 41 | 46 | 1000

# pre station, lane 1
io = PRE A1 stopper up        | 54 | 49 | 1000
io = PRE A1 lifter up         | 55 | 50 | 1000
io = PRE 1 lifter down        | 56 | 51 | 1000
# pre station, lane 2
io = PRE A2 stopper up        | 57 | 55 | 1000
io = PRE A2 lifter up         | 58 | 56 | 1000
io = PRE 2 lifter down        | 59 | 57 | 1000

# precisors: the input pairing is read from the label names (EXT_1/EXT_2, EXT_PRO_A_2/EXT_PRE_A_2)
io = PRO A1 precisor          | 60 | 52 | 1000 | optional
io = PRE A1 precisor          | 61 | 53 | 1000 | optional
io = PRO A2 precisor          | 62 | 68 | 1000 | optional
io = PRE A2 precisor          | 63 | 69 | 1000 | optional

# second PRE bank (OUT 64-70 / IN 65-73 carry the same labels as OUT 54-59): only one bank is wired per variant
io = PRE A1 stopper up (bank 2) | 64 | 65 | 1000 | optional
io = PRE A1 lifter up (bank 2)  | 65 | 66 | 1000 | optional
io = PRE 1 lifter down (bank 2) | 66 | 67 | 1000 | optional
io = PRE A2 stopper up (bank 2) | 68 | 71 | 1000 | optional
io = PRE A2 lifter up (bank 2)  | 69 | 72 | 1000 | optional
io = PRE 2 lifter down (bank 2) | 70 | 73 | 1000 | optional

# post station
io = POST A1 stopper up       | 72 | 77 | 1000
io = POST A1 lifter up        | 73 | 78 | 1000
io = POST 1 lifter down       | 74 | 79 | 1000
io = POST A2 stopper up       | 76 | 81 | 1000
io = POST A2 lifter up        | 77 | 82 | 1000
io = POST 2 lifter down       | 78 | 83 | 1000

# chuck vacuum: the switch only trips with a board or plate on the chuck
io = Chuck vac PRE A1         | 80 | 86 | 1000 | optional
io = Chuck vac PRE A2         | 81 | 87 | 1000 | optional
io = Chuck vac POST A1        | 82 | 88 | 1000 | optional
io = Chuck vac POST A2        | 83 | 89 | 1000 | optional
io = Chuck vac 1              | 84 | 90 | 1000 | optional
io = Chuck vac 2              | 85 | 91 | 1000 | optional
io = Chuck vac 3              | 86 | 92 | 1000 | optional
io = Chuck vac 4              | 87 | 93 | 1000 | optional

# door lock: only locks with the door closed
io = Door lock                | 11 | 15 | 1000 | optional
