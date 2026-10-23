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

# ---------- discrete IO loopback ----------
# io = <name> | <output> | <expected input> | <settle ms> | optional

io = Pre A1 stopper up        | 55 | 38 | 1000
io = Pre B1 stopper down      | 54 | 49 | 1000