"""Engine (XNA-style, row-vector) matrix math, mirrored by the C# FrameUniforms."""
import numpy as np, math

def normalize(v): v = np.asarray(v, float); return v / np.linalg.norm(v)

def look_at(pos, target, up):
    z = normalize(np.subtract(pos, target)); x = normalize(np.cross(up, z)); y = np.cross(z, x)
    m = np.identity(4)
    m[0, :3] = [x[0], y[0], z[0]]; m[1, :3] = [x[1], y[1], z[1]]; m[2, :3] = [x[2], y[2], z[2]]
    m[3, :3] = [-x @ pos, -y @ pos, -z @ pos]
    return m

def perspective(fov, aspect, near, far):
    ys = 1 / math.tan(fov / 2); xs = ys / aspect
    m = np.zeros((4, 4)); m[0, 0] = xs; m[1, 1] = ys; m[2, 2] = far / (near - far); m[2, 3] = -1; m[3, 2] = near * far / (near - far)
    return m

def translation(t): m = np.identity(4); m[3, :3] = t; return m
def rot_x(a): c, s = math.cos(a), math.sin(a); m = np.identity(4); m[1, 1] = c; m[1, 2] = s; m[2, 1] = -s; m[2, 2] = c; return m
def rot_z(a): c, s = math.cos(a), math.sin(a); m = np.identity(4); m[0, 0] = c; m[0, 1] = s; m[1, 0] = -s; m[1, 1] = c; return m

def transform_normal(v, m): return np.asarray(v, float) @ m[:3, :3]
def transform(v, m): return (np.append(v, 1.0) @ m)

def sun_direction(time_of_day, midday=0.5, season_angle=0.0):
    angle = 2 * math.pi * (time_of_day - midday)
    return normalize(transform_normal([0, 1, 0], rot_z(-angle) @ rot_x(season_angle)))

def shadow_matrices(light_dir, camera_pos, distance, depth_range, interval=2.0):
    """shadowModelView: camera-relative world pos -> light view; shadowProjection: ortho, z in [-1,1]."""
    center = np.floor(np.asarray(camera_pos) / interval) * interval
    rot = look_at(np.asarray(light_dir, float), np.zeros(3), np.array([0.0, 0.0, 1.0]))
    model_view = translation(np.asarray(camera_pos) - center) @ rot
    proj = np.identity(4); proj[0, 0] = 1 / distance; proj[1, 1] = 1 / distance; proj[2, 2] = -1 / depth_range
    return model_view, proj
