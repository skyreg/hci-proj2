﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using NativeWebSocket;
using SimpleJSON;
using Unity.Collections.LowLevel.Unsafe;

public class WebsocketZedVisualizer : MonoBehaviour
{
  // TODO: Parameterize
  public String host = "127.0.0.1";
  public String path = "/frames";

  // A holder for generated skeletons
  public Transform Skeletons;

  // How big should the joints appear?
  public float JointScale = 0.05F;
  // MM => M
  public float UnitConversion = 0.001F;
  // Use an avatar to visualize person
  public bool UseAvatar = false;
  // Use a skeleton to visualize person
  public bool UseSkeleton= true;
  // Avatars to choose from
  public List<GameObject> Avatars;


  WebSocket websocket;
  HashSet<string> existing_tracks;
  
  private string[] joint_names = {"Nose","LEye","REye","LEar","REar","LShoulder","RShoulder","LElbow","RElbow","LWrist","RWrist","LHip","RHip","LKnee","RKnee","LAnkle","RAnkle"};
  private int[,] joint_pair_idx = {{0,1},{0,2},{1,3},{2,4},{5,6},{5,7},{7,9},{6,8},{8,10},{11,12},{11,13},{13,15},{12,14},{14,16},{6,12},{5,11}};

  /// Map kinect joints to rocketbox avatar joints
  private static readonly Dictionary<string, string> coco_to_rocketbox = new Dictionary<string, string> {
    {"LShoulder", "Bip01/Bip01 Pelvis/Bip01 Spine/Bip01 Spine1/Bip01 Spine2/Bip01 L Clavicle/Bip01 L UpperArm"},
    {"LElbow", "Bip01/Bip01 Pelvis/Bip01 Spine/Bip01 Spine1/Bip01 Spine2/Bip01 L Clavicle/Bip01 L UpperArm/Bip01 L Forearm"},
    {"LWrist", "Bip01/Bip01 Pelvis/Bip01 Spine/Bip01 Spine1/Bip01 Spine2/Bip01 L Clavicle/Bip01 L UpperArm/Bip01 L Forearm/Bip01 L Hand"},
    {"RShoulder", "Bip01/Bip01 Pelvis/Bip01 Spine/Bip01 Spine1/Bip01 Spine2/Bip01 R Clavicle/Bip01 R UpperArm"},
    {"RElbow", "Bip01/Bip01 Pelvis/Bip01 Spine/Bip01 Spine1/Bip01 Spine2/Bip01 R Clavicle/Bip01 R UpperArm/Bip01 R Forearm"},
    {"RWrist", "Bip01/Bip01 Pelvis/Bip01 Spine/Bip01 Spine1/Bip01 Spine2/Bip01 R Clavicle/Bip01 R UpperArm/Bip01 R Forearm/Bip01 R Hand"},
    {"LHip", "Bip01/Bip01 Pelvis/Bip01 L Thigh"},
    {"LKnee", "Bip01/Bip01 Pelvis/Bip01 L Thigh/Bip01 L Calf/Bip01 L Foot"},
    {"LAnkle", "Bip01/Bip01 Pelvis/Bip01 L Thigh/Bip01 L Calf/Bip01 L Foot"},
    {"RHip", "Bip01/Bip01 Pelvis/Bip01 R Thigh"},
    {"RKnee", "Bip01/Bip01 Pelvis/Bip01 R Thigh/Bip01 R Calf/Bip01 R Foot"},
    {"RAnkle", "Bip01/Bip01 Pelvis/Bip01 R Thigh/Bip01 R Calf/Bip01 R Foot"},
    {"Nose", "Bip01/Bip01 Pelvis/Bip01 Spine/Bip01 Spine1/Bip01 Spine2/Bip01 Neck/Bip01 Head/Bip01 MNose"},
    //{"LEye", "HEAD"},
    //{"REye", "HEAD"},
    //{"LEar", "HEAD"},
    //{"REar", "HEAD"},
  };
  private Dictionary<string, Dictionary<string, Transform>> avatar_transforms = new Dictionary<string, Dictionary<string, Transform>> ();

  // Per the GameObject.CreatePrimitive documentation:
  private SphereCollider sphere_collider;

  // Start is called before the first frame update
  async void Start()
  {
    websocket = new WebSocket("ws://" + host + ":8888" + path);

    websocket.OnOpen += () =>
    {
      Debug.Log("Connection open!");
    };

    websocket.OnError += (e) =>
    {
      Debug.Log("Error! " + e);
    };

    websocket.OnClose += (e) =>
    {
      Debug.Log("Connection closed!");
    };

    websocket.OnMessage += (bytes) =>
    {
      //Debug.Log("OnMessage!");
      //Debug.Log(bytes);

      // getting the message as a string
      var message = System.Text.Encoding.UTF8.GetString(bytes);
      var N = JSON.Parse(message);
      var people = N["people"];
      HashSet<string> current_tracks = new HashSet<string>();
      foreach(KeyValuePair<string, SimpleJSON.JSONNode> person in people) {
        string id = person.Key;
        current_tracks.Add(id);
        FindOrCreateTrack(id, person.Value);
      }
      if (existing_tracks != null) {
        existing_tracks.ExceptWith(current_tracks);
        foreach(string old_id in existing_tracks) {
          RemoveTrack(old_id);
        }
      }
      existing_tracks = current_tracks;
    };

    // waiting for messages
    await websocket.Connect();
  }

  protected string IdToTrackId(string id) {
    return "track-"+id;
  }

  protected Transform FindTrack(string id) {
    return Skeletons.Find(IdToTrackId(id));
  }

  protected Transform FindOrCreateTrack(string id, SimpleJSON.JSONNode person) {
    SimpleJSON.JSONNode joints = person["keypoints"];
    if (!avatar_transforms.ContainsKey(id)) {
      avatar_transforms[id] = new Dictionary<string, Transform>();
    }
    Transform track = FindTrack(id);
    if (!track) {
      GameObject o = new GameObject(IdToTrackId(id));
      o.transform.parent = Skeletons;
      track = o.transform; //we can add collider/mesh here
    }

    Transform skeleton = track.Find("Skeleton");
    if (UseSkeleton) {
      if (!skeleton) {
        GameObject o = new GameObject();
        o.transform.parent = track;
        o.name = "Skeleton";
        skeleton = o.transform;
        // position at parent
        skeleton.localPosition = new Vector3(0,0,0);
      }
      skeleton.gameObject.SetActive(true);
    } else {
      skeleton.gameObject.SetActive(false);
    }

    Transform avatar = track.Find("Avatar");
    if (UseAvatar) {
      if (!avatar) {
        // TODO: randomize
        GameObject o = Instantiate(Avatars[0], new Vector3(0, 0, 0), Quaternion.identity);
        o.name = "Avatar";
        o.transform.parent = track;
        avatar = o.transform;
        // position at parent
        avatar.localPosition = new Vector3(0,0,0);
        avatar.localRotation = new Quaternion(0,0,0, 0);
        Transform Bip01 = avatar.Find("Bip01");
        Bip01.localPosition = new Vector3(0,0,0);
        Bip01.localRotation = new Quaternion(0,0,0, 0);
        FindAvatarTransforms(id, avatar);
      }
      avatar.gameObject.SetActive(true);
    } else if (avatar) {
      avatar.gameObject.SetActive(false);
    }
    
    // Position the track at the nose
    PositionJoint("Nose", track, joints["Nose"], global: true);
    track.rotation = Quaternion.Euler(0, 0, 0);
    //Debug.Log(avatar_transforms[id]["PELVIS"].localRotation);
    //avatar_transforms[id]["PELVIS"].localRotation = Quaternion.identity;

    float [] x_array = new float[20];
    float [] y_array = new float[20];
    float [] z_array = new float[20];
    int count_joints = 0;  
    // TODO: find medians and kick out the outlier points

    foreach (KeyValuePair<string, SimpleJSON.JSONNode> joint in joints) {
      if (joint.Value[9] == 0) { continue; }
      if (joint.Value[2] >= 0 && joint.Value[2] <= 0.1) {
        continue;
      }
      if (UseAvatar) {
        PoseAvatarJoint(avatar, id, joint.Key, joint.Value);
      }
      if (UseSkeleton) {
        PositionAndColorJoint(id, joint.Key, FindOrCreateJoint(skeleton, joint.Key), joint.Value);
      }
      
      x_array[count_joints] = joint.Value[0];
      y_array[count_joints] = joint.Value[1];
      z_array[count_joints] = joint.Value[2];
      count_joints += 1;

    }
    
    if(skeleton.Find("Boundary") == null)
    {
      //Debug.Log("track-" + id);
      float avgx = 0;
      float avgz = 0;
      int count = 0;
      foreach (KeyValuePair<string, SimpleJSON.JSONNode> joint in joints) {
        if(joint.Key == "LAnkle" || joint.Key == "RAnkle"){
          avgx += joint.Value[0];
          avgz += joint.Value[2];
          count+=1;
          //Debug.Log(joint.Key + " " + joint.Value[0] + " " + joint.Value[2]);
        }
      }
      if(avgz != 0){
        float avg1 = avgx/count;
        float avg2 = avgz/count;
        //Debug.Log("avg1: " + avg1 + " avg2: " + avg2);
        Vector3 pos = CameraToUnity(UnitConversion*avg1, (float)1.5, UnitConversion*avg2);
        //Debug.Log("pos: " + pos);
        GameObject boundary = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        boundary.name = "Boundary";
        boundary.transform.position = pos;
        boundary.transform.localScale = new Vector3(1.8288f, 0.000000001f, 1.8288f);
        boundary.transform.parent = skeleton;
        boundary.gameObject.GetComponent<Renderer>().material.color = Color.green;
      }
    }

    float median_x = 0;
    float median_y = 0;
    float median_z = 0;
    int index = 0;
    if (count_joints % 2 == 0)
    {
      index = count_joints / 2;
    }

    else
    {
      index = (count_joints + 1) /2; 
    }

    median_x = x_array[index];
    median_y = y_array[index];
    median_z = z_array[index];

    //Vector3 median_pos = CameraToUnity(UnitConversion*median_x, UnitConversion*median_y, UnitConversion*median_z);
    Vector3 median_pos = skeleton.Find("Boundary").transform.position;
    bool has_overlap = false;
    foreach (Transform other_skeleton in Skeletons) {
        if (other_skeleton != skeleton) {
            float [] other_x_array = new float[20];
            float [] other_y_array = new float[20];
            float [] other_z_array = new float[20];
            int other_count_joints = 0;  
            Transform next_skeleton = other_skeleton.Find("Skeleton");
            foreach (KeyValuePair<string, string> x in coco_to_rocketbox) {
                //Debug.Log("find " + x.Key);
                Transform found_joint = next_skeleton.Find(x.Key);
                if (found_joint){
                  //Debug.Log("found " + x.Key + " at " + found_joint.position);
                  Vector3 found_position = found_joint.position;
                  if (found_position.z >= 0 && found_position.z <= 0.1) {
                    continue; 
                  }
                  other_x_array[other_count_joints] = found_position.x;
                  other_y_array[other_count_joints] = found_position.y;
                  other_z_array[other_count_joints] = found_position.z;
                  other_count_joints += 1;
                }  
            }


            float other_median_x = 0;
            float other_median_y = 0;
            float other_median_z = 0;
            int other_index = 0;
            if (other_count_joints % 2 == 0)
            {
              other_index = other_count_joints / 2;
            }


            else
            {
              other_index = (other_count_joints + 1) /2; 
            }

            other_median_x = other_x_array[other_index];
            other_median_y = other_y_array[other_index];
            other_median_z = other_z_array[other_index];
            //Vector3 check_median_pos = CameraToUnity(UnitConversion*other_median_x, UnitConversion*other_median_y, UnitConversion*other_median_z);
            Vector3 check_median_pos = next_skeleton.Find("Boundary").position;
            float distance = Vector3.Distance(median_pos, check_median_pos);
            //Debug.Log("distance: " + distance);
            //Debug.Log(median_pos + " " + other_skeleton.position + " " + distance);
            //1.8288
            skeleton.Find("Boundary").gameObject.GetComponent<Renderer>().material.color = Color.green;
            if (distance <= 1.8288 && distance != 0) {
                has_overlap = true;
                skeleton.Find("Boundary").gameObject.GetComponent<Renderer>().material.color = Color.red;
                break;
            }
        }
        }

    foreach (KeyValuePair<string, SimpleJSON.JSONNode> joint in joints) {
      if (joint.Value[9] == 0) { continue; }
      if (UseAvatar) {
        PoseAvatarJoint(avatar, id, joint.Key, joint.Value);
      }
      if (UseSkeleton) {
        PositionAndColorJoint(id, joint.Key, FindOrCreateJoint(skeleton, joint.Key), joint.Value);
      }
    }

    

    // Compute joint rotations and 
    if (UseSkeleton) {
      SkeletalLines(id, skeleton, has_overlap);
    }

    return track;
  }

  /// <summary>Render lines between skeletal joints</summary>
  protected void SkeletalLines(string id, Transform skeleton, bool has_overlap) {
    for(int i = 0; i < joint_pair_idx.GetLength(0); i++) {
      string a = joint_names[joint_pair_idx[i,0]];
      string b = joint_names[joint_pair_idx[i,1]];
      Color color = GoldenColor(id, v: 0.5f);
      if (has_overlap) {
        color = Color.white;
      }
      RenderLine(skeleton, color, a, b);
    }
  }

  /// <summary>Render a line between skeletal joints</summary>
  protected void RenderLine(Transform skeleton, Color color, string a, string b) {
    string name = a + "-" + b;
    //Debug.Log(name);
    Transform line = skeleton.Find(name);
    LineRenderer renderer = null;
    if (!line) {
      GameObject o = new GameObject(name);
      o.transform.parent = skeleton;
      line = o.transform;
      renderer = o.AddComponent<LineRenderer>();
      renderer.material = new Material(Shader.Find("Sprites/Default"));
      renderer.widthMultiplier = 0.02f;
      renderer.positionCount = 2;
    }
    if (!renderer) {
      renderer = line.gameObject.GetComponent<LineRenderer>();
    }
    Transform t_a = skeleton.Find(a);
    Transform t_b = skeleton.Find(b);
    //if (!t_a) {
    //  Debug.LogError("Could not find joint: " + a);
    //}
    //if (!t_b) {
    //  Debug.LogError("Could not find joint: " + b);
    //}
    if (!(t_a && t_b)) {
      return;
    }
    Vector3[] points = new Vector3[] {t_a.position, t_b.position};
    renderer.startColor = color;
    renderer.endColor = color;
    renderer.SetPositions(points);
  }

  protected Transform FindOrCreateJoint(Transform skeleton, string joint_name) {
    Transform joint = skeleton.Find(joint_name);
    if (!joint) {
      GameObject o = GameObject.CreatePrimitive(PrimitiveType.Sphere);
      // If the standard shader is not assigned, color assignment in WebGL fails
      // /frames information is in millimeters
      // unity uses metric units 1 meter per unit 
      // take the average of a person's key points - center of mass - create a physics collider - unity capsule collider - set radius - can check for collisions
      // mesh shows up but no physical properties - colliders can or cannot intersect
      // create a mesh (cylinder or sphere) creates a covid bubble - 3D alpha property on shader and it's how we render it, color or texture applied, could draw a circle, 2D; 
      // can check interactions between colliders
      o.GetComponent<Renderer>().material.shader = Shader.Find("Standard");
      o.name = joint_name;
      o.transform.localScale = new Vector3(JointScale, JointScale, JointScale);
      o.transform.parent = skeleton;
      joint = o.transform;
    }
    return joint;
  }

  /// <summary>Returns a unique color object given the track ID using the golden angle</summary>
  protected Color GoldenColor(string id, float s = 0.85F, float v = 0.7F) {
    float h = ((Convert.ToInt32(id) * 137.0F) % 360)/360;
    return Color.HSVToRGB(h,s,v);
  }

  /// <summary>Given a single joint for a given avatar, position the joint.</summary>
  protected void PoseAvatarJoint(Transform avatar, string id, string joint_name, SimpleJSON.JSONNode data) {
    if (!avatar_transforms[id].ContainsKey(joint_name)) {
      //Debug.LogWarning("Could not transform missing joint " + joint_name + ", check FindAvatarTransforms.");
      return;
    }
    // TODO: rotation only?
    bool global = false;
    if (joint_name == "PELVIS") {
      global = true;
    }
    RotateJoint(joint_name, avatar_transforms[id][joint_name], data, global: global);
  }

  /// <summary>Given a single joint for a given track (id), create and color the points.</summary>
  protected void PositionAndColorJoint(string id, string joint_name, Transform joint, SimpleJSON.JSONNode data) {
    Color color = GoldenColor(id);
    color.a = data[3];
    joint.gameObject.GetComponent<Renderer>().material.color = color;
    PositionJoint(joint_name, joint, data, global: true);
  }

  /// <summary>Position an individual joint</summary>
  protected void PositionJoint(string joint_name, Transform joint, SimpleJSON.JSONNode data, bool global = false) {
    Vector3 pos = CameraToUnity(UnitConversion*data[0], UnitConversion*data[1], UnitConversion*data[2]);
    if (global) {
      joint.position = pos;
    } else {
      joint.localPosition = pos;
    }
  }

  /// <summary>Rotate an individual joint</summary>
  protected void RotateJoint(string joint_name, Transform joint, SimpleJSON.JSONNode data, bool global = false) {
    Quaternion q = CameraToUnity(joint_name, data[4], data[5], data[6], data[3]);
    if (global) {
      joint.localRotation = q;
    } else {
      joint.localRotation *= q;
    }
  }


  /// <summary>Remove a track by kinect-provided id</summary>
  protected void RemoveTrack(string id) {
    Destroy(FindTrack(id).gameObject);
  }

  /// <summary>Cache all the avatars' joint transforms</summary>
  protected void FindAvatarTransforms(string id, Transform avatar) {
    foreach(KeyValuePair<string, string> entry in coco_to_rocketbox) {
      Transform joint = avatar.Find(entry.Value);
      if (!joint) {
        Debug.LogError("Cannot find joint " + entry.Value  + " on " + avatar);
      } else {
        avatar_transforms[id].Add(entry.Key, joint);
      }
    }
  }

  /// <summary>Convert a camera point to Unity point</summary>
  /// https://docs.microsoft.com/en-us/azure/kinect-dk/coordinate-systems
  /// The frame mapping is:
  ///            DEPTH    UNITY
  ///  forward     z        z
  ///  up          -y       y
  ///  right       x        x
  protected Vector3 CameraToUnity(float x, float y, float z) {
    return new Vector3(x,-y,z);
  }

  /// <summary>Convert a camera point to Unity point, mirroring it in the viewport</summary>
  /// Joints are in axis orientation, and therefore must be transformed to world coordinates
  /// differently depending on which join is being transformed
  /// https://docs.microsoft.com/en-us/azure/kinect-dk/body-joints
  protected Quaternion CameraToUnity(string joint_name, float x, float y, float z, float w) {
    // On the torso and left side of the body the mapping is:
    //           joint   unity
    // forward     -y      y
    // up           x      -x
    // right        z      -z
    return new Quaternion(-x, -y, -z, w);
    return Quaternion.identity;
  }

  void Update()
  {
    #if !UNITY_WEBGL || UNITY_EDITOR
      websocket.DispatchMessageQueue();
    #endif
  }

  private async void OnApplicationQuit()
  {
    await websocket.Close();
  }

}
