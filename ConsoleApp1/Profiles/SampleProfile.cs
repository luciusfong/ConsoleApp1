namespace DeviceScanner.Profiles
{
    /// <summary>Example profile, written to the Profiles folder the first time the scanner runs with none present.</summary>
    internal static class SampleProfile
    {
        public const string Text = @"# ==============================================================
# DeviceScanner machine profile
# EXAMPLE built from the SD1X block in FactoryReset - edit it to match the real machine.
# To test another machine, copy this file and change the values. Nothing else needs editing.
#
# Format:  key = value        '#' starts a comment
# Repeatable keys: home.programs, ip.expect, com.expect, io
# ==============================================================

name = SD1X

# ---------- controller ----------
zmc.ip        = 192.168.0.11
zmc.init      = INITSYSTEM
home.programs = GLHOME, GRHOME           # started together, in this order
home.axes     = 6, 7, 8, 9, 10, 11       # axes to wait on; leave empty to auto-discover

# ---------- devices that must be present ----------
# ip.expect  = <ip>   | <device>    device: CL3 (Keyence) | OPT light | Camera GigE | ZMC
# com.expect = <port> | <device>    device: ITV regulator | MT balance | OPT light | LC18 light
#                                           Temp TK | Temp TX | Temp TM2 | Temp TM4
# The ZMC at zmc.ip is checked automatically.
com.slaves = 1, 2, 3, 4                  # temperature controller slave addresses to try

ip.expect  = 192.168.0.101 | Camera GigE
ip.expect  = 192.168.0.102 | Camera GigE
ip.expect  = 192.168.1.11  | CL3 (Keyence)

com.expect = COM6 | LC18 light
com.expect = COM1 | ITV regulator
com.expect = COM7 | ITV regulator
com.expect = COM2 | ITV regulator
com.expect = COM8 | ITV regulator
com.expect = COM5 | Temp TX
com.expect = COM5 | Temp TK
# com.expect = COM3 | MT balance         # weigh is StartUpEnable=false in FactoryReset

# ---------- discrete IO loopback ----------
# io = <name> | <output> | <expected input> | <settle ms> | optional
#   expected input blank or '-' : do not check a pair, just report which inputs react
#   settle ms blank             : use IoSettleMs from ScanConfig
#   optional                    : a failure is shown as WARN and does not fail the run
# Outputs really switch while this runs.

io = Pump 1 press             | 4  | 0  | 1000
io = Pump 1 vacuum            | 5  | 1  | 1000
io = Pump 2 press             | 7  | 5  | 1000 | optional    # FactoryReset labels input 5 PUMP_PRESS_ON_1, probably should be _2
io = Pump 2 vacuum            | 8  | 6  | 1000 | optional    # same for input 6

io = Door lock                | 11 | 15 | 1000

io = SMEMA 1 up (MC ready)    | 36 | 32 | 1000 | optional    # needs an SMEMA loopback or the upstream machine
io = SMEMA 1 down (BD ready)  | 37 | 33 | 1000 | optional
io = SMEMA 2 up (MC ready)    | 51 | 34 | 1000 | optional
io = SMEMA 2 down (BD ready)  | 52 | 35 | 1000 | optional

io = Station 1 stopper up     | 39 | 38 | 1000
io = Station 1 lifter up      | 40 | 39 | 1000
io = Station 1 lifter down    | 41 | 40 | 1000
io = Station 1 chuck vac high | 42 | 41 | 1000
io = Station 1 chuck vac low  | 53 | 41 | 1000
io = Station 1 clean press    | 43 | 22 | 1000
io = Station 1 clean vac      | 44 | 22 | 1000

io = Station 2 stopper up     | 45 | 44 | 1000
io = Station 2 lifter up      | 46 | 45 | 1000
io = Station 2 lifter down    | 47 | 46 | 1000
io = Station 2 chuck vac high | 48 | 47 | 1000
io = Station 2 chuck vac low  | 54 | 47 | 1000
io = Station 2 clean press    | 49 | 23 | 1000
io = Station 2 clean vac      | 50 | 23 | 1000

io = Nozzle clean             | 55 | 52 | 1000
";
    }
}
