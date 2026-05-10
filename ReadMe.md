# SDL3 2 DSU

Translates SDL3 supported controller inputs to xinput and translates motion data to a CemuHook compatible UDP server. 

Mainly for emulator use, though it does allow some controllers to work as a basic xinput pad.

# Aims

The goal of the project is to take controllers that arent compatible with SDL2 and make them able to be used with most modern/popular emulators.

Steam Input does most of this job already, but doesn't expose gyro input directly to the emulators in anyway, which irks me. Gyro to mouse is not really equivalent, especially in scenarios where the gyro isnt just used for aiming.

# Currently working

* Pro controller 2 works with emulators, back buttons are bound to a and b by default

# Goals
* GUI for remapping controller inputs and showing cemuhook server
* Clean up code massively (currently lot of vibecoding, code is pretty naff)
* Support steam controller 2 (If valve would ever ship mine :/)


# To use

* Run ```dotnet run```
* Connect CemuHook to 127.0.0.1 and port 26760
* Have fun 