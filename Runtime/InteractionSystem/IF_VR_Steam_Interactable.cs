﻿//======= Copyright (c) iNTERVR, All rights reserved. =========================
// Override InteractionSystem from SteamVR Unity Plugin of Valve Corporation
//
// Purpose: This object will get hover events and can be attached to the hands
//
//=============================================================================

using UnityEngine;
using System.Collections.Generic;
using Valve.VR;
using EcsRx.Zenject;
using EcsRx.Infrastructure.Extensions;
using InterVR.IF.VR.Plugin.Steam.Modules;

namespace InterVR.IF.VR.Plugin.Steam.InteractionSystem
{
    //-------------------------------------------------------------------------
    public class IF_VR_Steam_Interactable : MonoBehaviour
    {
        [Tooltip("Activates an action set on attach and deactivates on detach")]
        public SteamVR_ActionSet activateActionSetOnAttach;

        [Tooltip("Hide the whole hand on attachment and show on detach")]
        public bool hideHandOnAttach = true;

        [Tooltip("Hide the skeleton part of the hand on attachment and show on detach")]
        public bool hideSkeletonOnAttach = false;

        [Tooltip("Hide the controller part of the hand on attachment and show on detach")]
        public bool hideControllerOnAttach = false;

        [Tooltip("The integer in the animator to trigger on pickup. 0 for none")]
        public int handAnimationOnPickup = 0;

        [Tooltip("The range of motion to set on the skeleton. None for no change.")]
        public SkeletalMotionRangeChange setRangeOfMotionOnPickup = SkeletalMotionRangeChange.None;

        public delegate void OnAttachedToHandDelegate(IF_VR_Steam_Hand hand);
        public delegate void OnDetachedFromHandDelegate(IF_VR_Steam_Hand hand);
        public delegate bool OnHandHoverBeginDelegate(IF_VR_Steam_Interactable interactable, IF_VR_Steam_Hand hand);
        public delegate void OnHandHoverEndDelegate(IF_VR_Steam_Interactable interactable, IF_VR_Steam_Hand hand);
        public delegate void OnUpdateDelegate(IF_VR_Steam_Interactable interactable);
        public delegate void OnDestroyDelegate(IF_VR_Steam_Interactable interactable);
        public delegate void OnDisableDelegate(IF_VR_Steam_Interactable interactable);

        public event OnAttachedToHandDelegate onAttachedToHand;
        public event OnDetachedFromHandDelegate onDetachedFromHand;
        public event OnHandHoverBeginDelegate onHandHoverBegin;
        public event OnHandHoverEndDelegate onHandHoverEnd;
        public event OnUpdateDelegate onUpdate;
        public event OnDestroyDelegate onDestroy;
        public event OnDisableDelegate onDisable;


        [Tooltip("Specify whether you want to snap to the hand's object attachment point, or just the raw hand")]
        public bool useHandObjectAttachmentPoint = true;

        public bool attachEaseIn = false;
        [HideInInspector]
        public AnimationCurve snapAttachEaseInCurve = AnimationCurve.EaseInOut(0.0f, 0.0f, 1.0f, 1.0f);
        public float snapAttachEaseInTime = 0.15f;

        public bool snapAttachEaseInCompleted = false;


        // [Tooltip("The skeleton pose to apply when grabbing. Can only set this or handFollowTransform.")]
        [HideInInspector]
        public SteamVR_Skeleton_Poser skeletonPoser;

        [Tooltip("Should the rendered hand lock on to and follow the object")]
        public bool handFollowTransform = true;


        [Tooltip("Set whether or not you want this interactible to highlight when hovering over it")]
        public bool highlightOnHover = true;
        protected MeshRenderer[] highlightRenderers;
        protected MeshRenderer[] existingRenderers;
        protected GameObject highlightHolder;
        protected SkinnedMeshRenderer[] highlightSkinnedRenderers;
        protected SkinnedMeshRenderer[] existingSkinnedRenderers;
        protected static Material highlightMat;
        [Tooltip("An array of child gameObjects to not render a highlight for. Things like transparent parts, vfx, etc.")]
        public GameObject[] hideHighlight;

        [Tooltip("Higher is better")]
        public int hoverPriority = 0;

        [System.NonSerialized]
        public IF_VR_Steam_Hand attachedToHand;

        [System.NonSerialized]
        public List<IF_VR_Steam_Hand> hoveringHands = new List<IF_VR_Steam_Hand>();
        public IF_VR_Steam_Hand hoveringHand
        {
            get
            {
                if (hoveringHands.Count > 0)
                    return hoveringHands[0];
                return null;
            }
        }


        public bool isDestroying { get; protected set; }
        public bool isHovering { get; protected set; }
        public bool wasHovering { get; protected set; }


        private void Awake()
        {
            skeletonPoser = GetComponent<SteamVR_Skeleton_Poser>();
        }

        protected virtual void Start()
        {
            if (highlightMat == null)
#if UNITY_URP
                highlightMat = (Material)Resources.Load("SteamVR_HoverHighlight_URP", typeof(Material));
#else
                highlightMat = (Material)Resources.Load("SteamVR_HoverHighlight", typeof(Material));
#endif

            if (highlightMat == null)
                Debug.LogError("<b>[SteamVR Interaction]</b> Hover Highlight Material is missing. Please create a material named 'SteamVR_HoverHighlight' and place it in a Resources folder", this);

            if (skeletonPoser != null)
            {
                if (useHandObjectAttachmentPoint)
                {
                    //Debug.LogWarning("<b>[SteamVR Interaction]</b> SkeletonPose and useHandObjectAttachmentPoint both set at the same time. Ignoring useHandObjectAttachmentPoint.");
                    useHandObjectAttachmentPoint = false;
                }
            }

            var componentBuilder = EcsRxApplicationBehaviour.Instance.Container.Resolve<IF_VR_Steam_IComponentBuilder>();
            componentBuilder.Build(this);
        }

        protected virtual bool ShouldIgnoreHighlight(Component component)
        {
            return ShouldIgnore(component.gameObject);
        }

        protected virtual bool ShouldIgnore(GameObject check)
        {
            for (int ignoreIndex = 0; ignoreIndex < hideHighlight.Length; ignoreIndex++)
            {
                if (check == hideHighlight[ignoreIndex])
                    return true;
            }

            return false;
        }

        protected virtual void CreateHighlightRenderers()
        {
            existingSkinnedRenderers = this.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            highlightHolder = new GameObject("Highlighter");
            highlightSkinnedRenderers = new SkinnedMeshRenderer[existingSkinnedRenderers.Length];

            for (int skinnedIndex = 0; skinnedIndex < existingSkinnedRenderers.Length; skinnedIndex++)
            {
                SkinnedMeshRenderer existingSkinned = existingSkinnedRenderers[skinnedIndex];

                if (ShouldIgnoreHighlight(existingSkinned))
                    continue;

                GameObject newSkinnedHolder = new GameObject("SkinnedHolder");
                newSkinnedHolder.transform.parent = highlightHolder.transform;
                SkinnedMeshRenderer newSkinned = newSkinnedHolder.AddComponent<SkinnedMeshRenderer>();
                Material[] materials = new Material[existingSkinned.sharedMaterials.Length];
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    materials[materialIndex] = highlightMat;
                }

                newSkinned.sharedMaterials = materials;
                newSkinned.sharedMesh = existingSkinned.sharedMesh;
                newSkinned.rootBone = existingSkinned.rootBone;
                newSkinned.updateWhenOffscreen = existingSkinned.updateWhenOffscreen;
                newSkinned.bones = existingSkinned.bones;

                highlightSkinnedRenderers[skinnedIndex] = newSkinned;
            }

            MeshFilter[] existingFilters = this.GetComponentsInChildren<MeshFilter>(true);
            existingRenderers = new MeshRenderer[existingFilters.Length];
            highlightRenderers = new MeshRenderer[existingFilters.Length];

            for (int filterIndex = 0; filterIndex < existingFilters.Length; filterIndex++)
            {
                MeshFilter existingFilter = existingFilters[filterIndex];
                MeshRenderer existingRenderer = existingFilter.GetComponent<MeshRenderer>();

                if (existingFilter == null || existingRenderer == null || ShouldIgnoreHighlight(existingFilter))
                    continue;

                GameObject newFilterHolder = new GameObject("FilterHolder");
                newFilterHolder.transform.parent = highlightHolder.transform;
                MeshFilter newFilter = newFilterHolder.AddComponent<MeshFilter>();
                newFilter.sharedMesh = existingFilter.sharedMesh;
                MeshRenderer newRenderer = newFilterHolder.AddComponent<MeshRenderer>();

                Material[] materials = new Material[existingRenderer.sharedMaterials.Length];
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    materials[materialIndex] = highlightMat;
                }
                newRenderer.sharedMaterials = materials;

                highlightRenderers[filterIndex] = newRenderer;
                existingRenderers[filterIndex] = existingRenderer;
            }
        }

        protected virtual void UpdateHighlightRenderers()
        {
            if (highlightHolder == null)
                return;

            for (int skinnedIndex = 0; skinnedIndex < existingSkinnedRenderers.Length; skinnedIndex++)
            {
                SkinnedMeshRenderer existingSkinned = existingSkinnedRenderers[skinnedIndex];
                SkinnedMeshRenderer highlightSkinned = highlightSkinnedRenderers[skinnedIndex];

                if (existingSkinned != null && highlightSkinned != null && attachedToHand == false)
                {
                    highlightSkinned.transform.position = existingSkinned.transform.position;
                    highlightSkinned.transform.rotation = existingSkinned.transform.rotation;
                    highlightSkinned.transform.localScale = existingSkinned.transform.lossyScale;
                    highlightSkinned.localBounds = existingSkinned.localBounds;
                    highlightSkinned.enabled = isHovering && existingSkinned.enabled && existingSkinned.gameObject.activeInHierarchy;

                    int blendShapeCount = existingSkinned.sharedMesh.blendShapeCount;
                    for (int blendShapeIndex = 0; blendShapeIndex < blendShapeCount; blendShapeIndex++)
                    {
                        highlightSkinned.SetBlendShapeWeight(blendShapeIndex, existingSkinned.GetBlendShapeWeight(blendShapeIndex));
                    }
                }
                else if (highlightSkinned != null)
                    highlightSkinned.enabled = false;

            }

            for (int rendererIndex = 0; rendererIndex < highlightRenderers.Length; rendererIndex++)
            {
                MeshRenderer existingRenderer = existingRenderers[rendererIndex];
                MeshRenderer highlightRenderer = highlightRenderers[rendererIndex];

                if (existingRenderer != null && highlightRenderer != null && attachedToHand == false)
                {
                    highlightRenderer.transform.position = existingRenderer.transform.position;
                    highlightRenderer.transform.rotation = existingRenderer.transform.rotation;
                    highlightRenderer.transform.localScale = existingRenderer.transform.lossyScale;
                    highlightRenderer.enabled = isHovering && existingRenderer.enabled && existingRenderer.gameObject.activeInHierarchy;
                }
                else if (highlightRenderer != null)
                    highlightRenderer.enabled = false;
            }
        }

        /// <summary>
        /// Called when a IF_VR_Steam_Hand starts hovering over this object
        /// </summary>
        protected virtual void OnHandHoverBegin(IF_VR_Steam_Hand hand)
        {
            wasHovering = isHovering;
            isHovering = true;

            hoveringHands.Add(hand);

            if (onHandHoverBegin != null)
            {
                if (onHandHoverBegin.Invoke(this, hand) == true)
                {
                    // consumed in there
                    return;
                }
            }

            if (highlightOnHover == true && wasHovering == false)
            {
                CreateHighlightRenderers();
                UpdateHighlightRenderers();
            }
        }


        /// <summary>
        /// Called when a IF_VR_Steam_Hand stops hovering over this object
        /// </summary>
        protected virtual void OnHandHoverEnd(IF_VR_Steam_Hand hand)
        {
            wasHovering = isHovering;

            hoveringHands.Remove(hand);

            if (hoveringHands.Count == 0)
            {
                isHovering = false;

                if (highlightOnHover && highlightHolder != null)
                    Destroy(highlightHolder);
            }

            onHandHoverEnd?.Invoke(this, hand);
        }

        protected virtual void Update()
        {
            if (highlightOnHover)
            {
                UpdateHighlightRenderers();

                if (isHovering == false && highlightHolder != null)
                    Destroy(highlightHolder);
            }

            onUpdate?.Invoke(this);
        }


        protected float blendToPoseTime = 0.1f;
        protected float releasePoseBlendTime = 0.2f;

        protected virtual void OnAttachedToHand(IF_VR_Steam_Hand hand)
        {
            if (activateActionSetOnAttach != null)
                activateActionSetOnAttach.Activate(hand.handType);

            if (onAttachedToHand != null)
            {
                onAttachedToHand.Invoke(hand);
            }

            if (skeletonPoser != null && hand.skeleton != null)
            {
                hand.skeleton.BlendToPoser(skeletonPoser, blendToPoseTime);
            }

            attachedToHand = hand;
        }

        protected virtual void OnDetachedFromHand(IF_VR_Steam_Hand hand)
        {
            if (activateActionSetOnAttach != null)
            {
                if (hand.otherHand == null || hand.otherHand.currentAttachedObjectInfo.HasValue == false ||
                    (hand.otherHand.currentAttachedObjectInfo.Value.interactable != null &&
                     hand.otherHand.currentAttachedObjectInfo.Value.interactable.activateActionSetOnAttach != this.activateActionSetOnAttach))
                {
                    activateActionSetOnAttach.Deactivate(hand.handType);
                }
            }

            if (onDetachedFromHand != null)
            {
                onDetachedFromHand.Invoke(hand);
            }


            if (skeletonPoser != null)
            {
                if (hand.skeleton != null)
                    hand.skeleton.BlendToSkeleton(releasePoseBlendTime);
            }

            attachedToHand = null;
        }

        protected virtual void OnDestroy()
        {
            isDestroying = true;

            if (attachedToHand != null)
            {
                attachedToHand.DetachObject(this.gameObject, false);
                attachedToHand.skeleton.BlendToSkeleton(0.1f);
            }

            if (highlightHolder != null)
                Destroy(highlightHolder);

            onDestroy?.Invoke(this);
        }


        protected virtual void OnDisable()
        {
            isDestroying = true;

            if (attachedToHand != null)
            {
                attachedToHand.ForceHoverUnlock();
            }

            if (highlightHolder != null)
                Destroy(highlightHolder);

            onDisable?.Invoke(this);
        }
    }
}
