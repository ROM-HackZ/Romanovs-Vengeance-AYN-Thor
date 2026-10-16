package org.romanovsthor.qa;

import android.os.SystemClock;
import android.view.InputDevice;
import android.view.InputEvent;
import android.view.KeyEvent;
import android.view.MotionEvent;
import java.lang.reflect.Method;

/** ADB-shell-only test driver. Never packaged into the game or installer. */
public final class ThorInputProbe {
    static Object manager;
    static Method inject;
    static long down;

    static MotionEvent.PointerProperties property(int id, int tool) {
        MotionEvent.PointerProperties p = new MotionEvent.PointerProperties();
        p.id = id; p.toolType = tool; return p;
    }
    static MotionEvent.PointerCoords point(float x, float y) {
        MotionEvent.PointerCoords p = new MotionEvent.PointerCoords();
        p.x = x; p.y = y; p.pressure = 1; p.size = .1f; return p;
    }
    static void send(int action, MotionEvent.PointerCoords[] points, int source) throws Exception {
        MotionEvent.PointerProperties[] properties = new MotionEvent.PointerProperties[points.length];
        for (int i = 0; i < properties.length; i++) properties[i] = property(i,
            source == InputDevice.SOURCE_TOUCHSCREEN ? MotionEvent.TOOL_TYPE_FINGER : MotionEvent.TOOL_TYPE_UNKNOWN);
        MotionEvent event = MotionEvent.obtain(down, SystemClock.uptimeMillis(), action, points.length,
            properties, points, 0, 0, 1, 1, 0, 0, source, 0);
        try {
            if (!((Boolean)inject.invoke(manager, event, 2))) throw new IllegalStateException("Input injection rejected");
        } finally { event.recycle(); }
    }
    static void two(String kind) throws Exception {
        float x = 1000, y = 540, initial = kind.equals("pinch-out") ? 140 : 260;
        send(MotionEvent.ACTION_DOWN, new MotionEvent.PointerCoords[]{ point(x-initial/2,y) }, InputDevice.SOURCE_TOUCHSCREEN);
        SystemClock.sleep(35);
        send(MotionEvent.ACTION_POINTER_DOWN | (1 << MotionEvent.ACTION_POINTER_INDEX_SHIFT),
            new MotionEvent.PointerCoords[]{point(x-initial/2,y),point(x+initial/2,y)}, InputDevice.SOURCE_TOUCHSCREEN);
        System.out.println("DOWN"); System.out.flush();
        for (int step=1;step<=18;step++) {
            float t=step/18f;
            float shift=kind.equals("pan") ? 200*t : kind.equals("pan-left") ? -200*t : 0;
            float span=kind.equals("pinch-in") ? initial-120*t : kind.equals("pinch-out") ? initial+120*t : initial;
            send(MotionEvent.ACTION_MOVE, new MotionEvent.PointerCoords[]{point(x+shift-span/2,y),point(x+shift+span/2,y)}, InputDevice.SOURCE_TOUCHSCREEN);
            SystemClock.sleep(kind.equals("two-tap") ? 3 : 16);
        }
        float shift=kind.equals("pan")?200:kind.equals("pan-left")?-200:0;
        float span=kind.equals("pinch-in")?140:kind.equals("pinch-out")?260:initial;
        send(MotionEvent.ACTION_POINTER_UP | (1 << MotionEvent.ACTION_POINTER_INDEX_SHIFT),
            new MotionEvent.PointerCoords[]{point(x+shift-span/2,y),point(x+shift+span/2,y)}, InputDevice.SOURCE_TOUCHSCREEN);
        send(MotionEvent.ACTION_UP, new MotionEvent.PointerCoords[]{point(x+shift-span/2,y)}, InputDevice.SOURCE_TOUCHSCREEN);
    }
    public static void main(String[] args) throws Exception {
        Class<?> type=Class.forName("android.hardware.input.InputManager");
        manager=type.getMethod("getInstance").invoke(null);
        inject=type.getMethod("injectInputEvent", InputEvent.class, int.class);
        down=SystemClock.uptimeMillis();
        if (args[0].equals("keys")) {
            int code=Integer.parseInt(args[1]);
            inject.invoke(manager,new KeyEvent(down,down,KeyEvent.ACTION_DOWN,code,0,0,0,0,0,InputDevice.SOURCE_GAMEPAD),2);
            System.out.println("DOWN"); System.out.flush();
            try { SystemClock.sleep(Integer.parseInt(args[2])); }
            finally { inject.invoke(manager,new KeyEvent(down,SystemClock.uptimeMillis(),KeyEvent.ACTION_UP,code,0,0,0,0,0,InputDevice.SOURCE_GAMEPAD),2); }
        } else if (args[0].equals("hat")) {
            MotionEvent.PointerCoords p=point(0,0);
            p.setAxisValue(MotionEvent.AXIS_HAT_X,Float.parseFloat(args[1]));
            p.setAxisValue(MotionEvent.AXIS_HAT_Y,Float.parseFloat(args[2]));
            try {
                send(MotionEvent.ACTION_MOVE,new MotionEvent.PointerCoords[]{p},InputDevice.SOURCE_JOYSTICK);
                System.out.println("DOWN"); System.out.flush();
                SystemClock.sleep(Integer.parseInt(args[3]));
            } finally { send(MotionEvent.ACTION_MOVE,new MotionEvent.PointerCoords[]{point(0,0)},InputDevice.SOURCE_JOYSTICK); }
        } else if (args[0].equals("axes") || args[0].equals("sequence")) {
            try {
                for(int start=1;start+6<args.length;start+=7) {
                    MotionEvent.PointerCoords p=point(Float.parseFloat(args[start]),Float.parseFloat(args[start+1]));
                    int[] axes={MotionEvent.AXIS_Z,MotionEvent.AXIS_RZ,MotionEvent.AXIS_LTRIGGER,MotionEvent.AXIS_RTRIGGER};
                    for (int i=0;i<axes.length;i++) p.setAxisValue(axes[i],Float.parseFloat(args[start+2+i]));
                    send(MotionEvent.ACTION_MOVE,new MotionEvent.PointerCoords[]{p},InputDevice.SOURCE_JOYSTICK);
                    if(start==1) { System.out.println("DOWN"); System.out.flush(); }
                    SystemClock.sleep(Integer.parseInt(args[start+6]));
                }
            }
            finally { send(MotionEvent.ACTION_MOVE,new MotionEvent.PointerCoords[]{point(0,0)},InputDevice.SOURCE_JOYSTICK); }
        } else two(args[0]);
    }
}
