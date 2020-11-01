import shutil
import os


def main():
    dir_path = os.path.dirname(os.path.realpath(__file__))
    try:
        bin = os.path.join(dir_path, "bin")
        debug = os.path.join(bin, "Debug")
        netstandard = os.path.join(debug, "netstandard2.0")
    except:
        print("Could not find directories")
        return
    desired_dll = None
    for file in os.listdir(netstandard):
        if file.endswith(".pdb"):
            desired_dll = file.replace(".pdb", ".dll")
    plugins = "C:\Program Files (x86)\Steam\steamapps\common\Poly Bridge 2\BepInEx\plugins"
    if desired_dll:
        desired_dll_src = os.path.join(netstandard, desired_dll)
        print(desired_dll_src)
        desired_dll_dest = os.path.join(plugins, desired_dll)
        print(desired_dll_dest)
        if os.path.isfile(desired_dll_src):
            print("About to copy")
            try:
                shutil.copy2(desired_dll_src, desired_dll_dest)
                print("Success")
            except:
                print("Nope")
        else:
            print("desired_dll_src is not a file - {}".format(desired_dll_src))
    else:
        print("desired_dll is None")


if __name__ == "__main__":
    main()
