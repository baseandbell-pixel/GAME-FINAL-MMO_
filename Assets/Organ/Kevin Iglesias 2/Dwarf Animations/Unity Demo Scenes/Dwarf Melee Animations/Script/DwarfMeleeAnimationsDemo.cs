//This script is for demo purposes only and is not required for the animations to work.
//Dwarf Melee Animations
//Kevin Iglesias
//www.keviniglesias.com

using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;


#if UNITY_EDITOR
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.Animations;
using System.IO;
#endif

namespace KevinIglesias
{
    [System.Serializable]
    public class DwarfMeleeAnimationEntry
    {
        public string displayName;
        public string folderName;
        public string folderPath;
        public int folderLevel;
        public AnimationClip clip;
        public GameObject[] prop;
        public bool disableSpineProxy;
        [HideInInspector] public Button animationButton;
    }
    
    [System.Serializable]
    public class DwarfMeleeCharacterEntry
    {
        public GameObject characterObject;
        public Transform characterTransform;
        public Animator animator;
        public Transform leftFoot;
        public Transform rightFoot;
        [HideInInspector] public Transform spineProxyBone;
        [HideInInspector] public Button characterButton;
    }
    
    [System.Serializable]
    public class DwarfMeleePropEntry
    {
        public string displayName;
        public GameObject[] models;
    }
    
    public class DwarfMeleeAnimationsDemo : MonoBehaviour
    {
        [Header("Camera")]
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private Camera targetCamera;
        [SerializeField] private float zoomStep = 5f;
        [SerializeField] private float minFOV = 35f;
        [SerializeField] private float maxFOV = 75f;
        [SerializeField] private float rotationSpeed = 120f;
        private float rotationInput = 0f;
        
        [Header("Base")]
        [SerializeField] private Transform baseT;
        [SerializeField] private Material baseMaterial;
        private float baseTreadmillMultiplier = 0.5f;
        private float baseOffsetX;
        private float baseOffsetY;
        private float baseUVRotation;
        
        [Header("Shadow")]
        [SerializeField] private Transform shadowT;
        [SerializeField] private SpriteRenderer shadowSprite;
        private float shadowFloorY = 0.001f;
        private float groundedScale = 0.6f;
        private float groundedAlpha = 0.78f;
        private float maxJumpHeight = 1f;
        private float airScale = 1f;
        private float airAlpha = 0.05f;
        private Transform currentLeftFoot;
        private Transform currentRightFoot; 
        private float shadowFootPadding = 0.35f;
        private float shadowMinScale = 0.75f;
        private float shadowMaxScale = 2.25f;
                
        [Header("Background")]
        [SerializeField] private Material backgroundMaterial;
        private Vector3 backgroundLightDir = new Vector3(0f, -0.1f, 1f);
        
        [Header("UI")]
        [SerializeField] private Transform zoomIn;
        [SerializeField] private Transform zoomOut;
        [SerializeField] private Transform leftArrow;
        [SerializeField] private Transform rightArrow;
        [SerializeField] private Color normalButtonColor = Color.white;
        [SerializeField] private Color selectedButtonColor = Color.black;
        [SerializeField] private Color normalTextColor = Color.black;
        [SerializeField] private Color selectedTextColor = Color.white;
        private Vector3 normalScale = Vector3.one;
        private Vector3 pressedScale = Vector3.one * 0.85f;
        
        [Header("Characters")]
        [SerializeField] private DwarfMeleeCharacterEntry[] availableCharacters;
        [SerializeField] private int currentCharacterIndex = 0;
        [SerializeField] private GameObject characterUIPrefab;
        [SerializeField] private Transform charactersUIRoot;
        [SerializeField] private Transform character;
        [SerializeField] private Animator animator;
        private Button currentSelectedCharacterButton;
        private bool characterInitialized = false;
        
        [Header("Character Settings")]
        private int currentHair = 0;
        [SerializeField] private DwarfMeleePropEntry[] hairs;
        [SerializeField] private Text currentHairText;
        private int currentBeard = 0;
        [SerializeField] private DwarfMeleePropEntry[] beards;
        [SerializeField] private Text currentBeardText;
        private int currentCape = 0;
        [SerializeField] private DwarfMeleePropEntry[] capes;
        [SerializeField] private Text currentCapeText;
        private int currentRightHandProp = 0;
        [SerializeField] private DwarfMeleePropEntry[] rightHandProps;
        [SerializeField] private Text currentRightHandPropText;
        private int currentLeftHandProp = 0;
        [SerializeField] private DwarfMeleePropEntry[] leftHandProps;
        [SerializeField] private Text currentLeftHandPropText;
        
        [Header("Animations")]
        [HideInInspector] [SerializeField] private DwarfMeleeAnimationEntry[] availableAnimations;
        [SerializeField] private Text currentAnimationText;
        [SerializeField] private Text currentAnimationFolderText;
        [SerializeField] private Transform animationsUIRoot;
        [SerializeField] private GameObject folderButtonPrefab;
        [SerializeField] private GameObject animationUIPrefab;
        private string currentAnimationName = "";
        private string currentUpperAnimationName = "";
        private int baseAnimationToPlay = 0;
        private int upperAnimationToPlay = -1;
        private AnimatorOverrideController overrideController;
        private string currentFolder = "";
        private Button currentSelectedAnimationButton;
        private GameObject[] currentBaseProps;
        private GameObject[] currentUpperProps;
        private int animationType = 0;
        [SerializeField] private Button baseTypeButton;
        [SerializeField] private Button upperTypeButton;

    ///UNITY DEFAULT FUNCTIONS
        private void Start()
        {
            ResetBase();

            CreateAnimationButtons();
            CreateCharacterButtons();

            if(availableCharacters != null && availableCharacters.Length > 0)
            {
                overrideController = new AnimatorOverrideController(availableCharacters[0].animator.runtimeAnimatorController);
            }

            for(int i = 0; i < availableCharacters.Length; i++)
            {
                if(availableCharacters[i].characterObject != null)
                {
                    availableCharacters[i].characterObject.SetActive(false);
                    
                    Transform[] transforms = availableCharacters[i].characterObject.GetComponentsInChildren<Transform>(true);

                    foreach(Transform t in transforms)
                    {
                        if (t.name == "B-spineProxy")
                        {
                            availableCharacters[i].spineProxyBone = t;
                            break;
                        }
                    }
                }
            }

            SelectCharacter(currentCharacterIndex);
            SelectCharacterButton(availableCharacters[currentCharacterIndex].characterButton);
            
            int defaultIndex = FindAnimationIndex("DwarfM@Idle01");

            if(defaultIndex >= 0)
            {
                PlayAnimation(defaultIndex, 0);

                if(defaultIndex < availableAnimations.Length)
                {
                    SelectAnimationButton(availableAnimations[defaultIndex].animationButton);
                }
            }
            
            SetButtonSelected(baseTypeButton, true);
            
            SelectHair();
            SelectBeard();
            SelectCape();
            SelectRightHandProp();
            SelectLeftHandProp();
        }
        private void Update()
        {
            baseT.position = new Vector3(character.position.x, 0, character.position.z);
            UpdateBlobShadowFromFeet();
            
            UpdateBaseMaterialOffset();

            Vector3 correctedLightDir = cameraPivot.rotation * backgroundLightDir;

            backgroundMaterial.SetVector("_LightDir", new Vector4(correctedLightDir.x, correctedLightDir.y, correctedLightDir.z, 0f));
            
            if(rotationInput != 0f)
            {
                cameraPivot.Rotate(new Vector3(0f, rotationInput * rotationSpeed * Time.deltaTime, 0f), Space.World);
            }
            
            character.position = new Vector3(0, character.position.y, 0);
        }
        private void FixedUpdate()
        {
            Vector3 correctedLightDir = cameraPivot.rotation * backgroundLightDir;
            backgroundMaterial.SetVector("_LightDir", new Vector4(correctedLightDir.x, correctedLightDir.y, correctedLightDir.z, 0f));
        }
        private void LateUpdate()
        {
            Vector3 correctedLightDir = cameraPivot.rotation * backgroundLightDir;
            backgroundMaterial.SetVector("_LightDir", new Vector4(correctedLightDir.x, correctedLightDir.y, correctedLightDir.z, 0f));
        }
        private void OnDisable()
        {
            ResetBase();
        }
    ///

    ///CHARACTER SELECTION
        public void SelectCharacter(int index)
        {
            if(availableCharacters == null || index < 0 || index >= availableCharacters.Length)
            {
                return;
            }

            if(characterInitialized && index == currentCharacterIndex)
            {
                return;
            }

            characterInitialized = true;
            currentCharacterIndex = index;

            for(int i = 0; i < availableCharacters.Length; i++)
            {
                DwarfMeleeCharacterEntry entry = availableCharacters[i];

                bool selected = i == index;

                if(entry.animator != null)
                {
                    entry.animator.runtimeAnimatorController = selected ? overrideController : null;
                }

                if(entry.characterObject != null)
                {
                    entry.characterObject.SetActive(selected);
                }
            }

            DwarfMeleeCharacterEntry selectedEntry = availableCharacters[index];

            character = selectedEntry.characterTransform;
            animator = selectedEntry.animator;
            
            currentLeftFoot = selectedEntry.leftFoot;
            currentRightFoot = selectedEntry.rightFoot;

            if(animator != null)
            {
                animator.Rebind();
                animator.Update(0f);
            }

            ResetProps();

            ResetBase();
            
            string equivalentAnimationName = currentAnimationName;
            if(index == 1)
            {
                equivalentAnimationName = currentAnimationName.Replace("DwarfM@", "DwarfF@");
            }else{
                equivalentAnimationName = currentAnimationName.Replace("DwarfF@", "DwarfM@");
            }
            int equivalentAnimation = FindAnimationIndex(equivalentAnimationName);

            if(equivalentAnimation >= 0)
            {
                PlayAnimation(equivalentAnimation, 0);
            }else{
                PlayAnimation(baseAnimationToPlay, 0);
            }
            
            string equivalentUpperAnimationName = currentUpperAnimationName;
            if(index == 1)
            {
                equivalentUpperAnimationName = currentUpperAnimationName.Replace("DwarfM@", "DwarfF@");
            }else{
                equivalentUpperAnimationName = currentUpperAnimationName.Replace("DwarfF@", "DwarfM@");
            }
            int equivalentUpperAnimation = FindAnimationIndex(equivalentUpperAnimationName);

            if(equivalentUpperAnimation >= 0)
            {
                PlayAnimation(equivalentUpperAnimation, 1);
            }else{
                if(upperAnimationToPlay >= 0)
                {
                    PlayAnimation(upperAnimationToPlay, 1);
                }else{
                    ClearUpperAnimation();
                }
            }
            
            string newFolder = currentFolder;
            if(index == 1)
            {
                newFolder = currentFolder.Replace("Male", "Female");
            }else{
                newFolder = currentFolder.Replace("Female", "Male");
            }
            ShowFolder(newFolder);
            
            SelectCurrentAnimationButtonInOpenFolder();
        }
        private void CreateCharacterButtons()
        {
            if(characterUIPrefab == null || charactersUIRoot == null || availableCharacters == null)
            {
                return;
            }

            for(int i = 0; i < availableCharacters.Length; i++)
            {
                int index = i;

                GameObject buttonGO = Instantiate(characterUIPrefab, charactersUIRoot);

                Button button = buttonGO.GetComponent<Button>();
                availableCharacters[i].characterButton = button;
                Image image = buttonGO.GetComponent<Image>();
                Text text = buttonGO.GetComponentInChildren<Text>();

                if(image != null)
                {
                    image.color = normalButtonColor;
                }

                if(text != null)
                {
                    text.color = normalTextColor;
                    text.text = availableCharacters[i].characterObject.name;
                }

                if(button != null)
                {
                    button.onClick.AddListener(() =>
                    {
                        SelectCharacter(index);
                        SelectCharacterButton(button);
                    });
                }

                buttonGO.SetActive(true);
            }
        }
        private void SelectCharacterButton(Button selectedButton)
        {
            if(currentSelectedCharacterButton != null)
            {
                SetButtonSelected(currentSelectedCharacterButton, false);
            }

            currentSelectedCharacterButton = selectedButton;

            if(currentSelectedCharacterButton != null)
            {
                SetButtonSelected(currentSelectedCharacterButton, true);
            }
        }
    ///
    
    ///CHARACTER SETTINGS SELECTION
    
    public void NextHair()
    {
        currentHair = (currentHair + 1) % hairs.Length;
        SelectHair();
    }
    public void PreviousHair()
    {
        currentHair--;

        if(currentHair < 0) currentHair = hairs.Length - 1;

        SelectHair();
    }
    public void SelectHair()
    {
        for(int i = 0; i < hairs.Length; i++)
        {
            for(int j = 0; j < hairs[i].models.Length; j++)
            {
                if(hairs[i].models[j] != null)
                {
                    hairs[i].models[j].SetActive(false);
                }
            }
        }
        
        for(int j = 0; j < hairs[currentHair].models.Length; j++)
        {
            if(hairs[currentHair].models[j] != null)
            {
                hairs[currentHair].models[j].SetActive(true);
            }
        }
        
        currentHairText.text = hairs[currentHair].displayName;
    }
    
    public void NextBeard()
    {
        currentBeard = (currentBeard + 1) % beards.Length;
        SelectBeard();
    }
    public void PreviousBeard()
    {
        currentBeard--;

        if(currentBeard < 0) currentBeard = beards.Length - 1;

        SelectBeard();
    }
    public void SelectBeard()
    {
        for(int i = 0; i < beards.Length; i++)
        {
            for(int j = 0; j < beards[i].models.Length; j++)
            {
                if(beards[i].models[j] != null)
                {
                    beards[i].models[j].SetActive(false);
                }
            }
        }
        for(int j = 0; j < beards[currentBeard].models.Length; j++)
        {
            if(beards[currentBeard].models[j] != null)
            {
                beards[currentBeard].models[j].SetActive(true);
            }
        }
        
        currentBeardText.text = beards[currentBeard].displayName;
    }
    
    public void NextCape()
    {
        currentCape = (currentCape + 1) % capes.Length;
        SelectCape();
    }
    public void PreviousCape()
    {
        currentCape--;

        if(currentCape < 0) currentCape = capes.Length - 1;

        SelectCape();
    }
    public void SelectCape()
    {
        for(int i = 0; i < capes.Length; i++)
        {
            for(int j = 0; j < capes[i].models.Length; j++)
            {
                if(capes[i].models[j] != null)
                {
                    capes[i].models[j].SetActive(false);
                }
            }
        }
        for(int j = 0; j < capes[currentCape].models.Length; j++)
        {
            if(capes[currentCape].models[j] != null)
            {
                capes[currentCape].models[j].SetActive(true);
            }
        }
        
        currentCapeText.text = capes[currentCape].displayName;
    }
    
    public void NextRightHandProp()
    {
        currentRightHandProp = (currentRightHandProp + 1) % rightHandProps.Length;
        SelectRightHandProp();
    }
    public void PreviousRightHandProp()
    {
        currentRightHandProp--;

        if(currentRightHandProp < 0) currentRightHandProp = rightHandProps.Length - 1;

        SelectRightHandProp();
    }
    public void SelectRightHandProp()
    {
        for(int i = 0; i < rightHandProps.Length; i++)
        {
            for(int j = 0; j < rightHandProps[i].models.Length; j++)
            {
                if(rightHandProps[i].models[j] != null)
                {
                    rightHandProps[i].models[j].SetActive(false);
                }
            }
        }
        for(int j = 0; j < rightHandProps[currentRightHandProp].models.Length; j++)
        {
            if(rightHandProps[currentRightHandProp].models[j] != null)
            {
                rightHandProps[currentRightHandProp].models[j].SetActive(true);
            }
        }
        
        currentRightHandPropText.text = rightHandProps[currentRightHandProp].displayName;
    }
    
    public void NextLeftHandProp()
    {
        currentLeftHandProp = (currentLeftHandProp + 1) % leftHandProps.Length;
        SelectLeftHandProp();
    }
    public void PreviousLeftHandProp()
    {
        currentLeftHandProp--;

        if(currentLeftHandProp < 0) currentLeftHandProp = leftHandProps.Length - 1;

        SelectLeftHandProp();
    }
    public void SelectLeftHandProp()
    {
        for(int i = 0; i < leftHandProps.Length; i++)
        {
            for(int j = 0; j < leftHandProps[i].models.Length; j++)
            {
                if(leftHandProps[i].models[j] != null)
                {
                    leftHandProps[i].models[j].SetActive(false);
                }
            }
        }
        for(int j = 0; j < leftHandProps[currentLeftHandProp].models.Length; j++)
        {
            if(leftHandProps[currentLeftHandProp].models[j] != null)
            {
                leftHandProps[currentLeftHandProp].models[j].SetActive(true);
            }
        }
        
        currentLeftHandPropText.text = leftHandProps[currentLeftHandProp].displayName;
    }

    ///ANIMATION SELECTION
        public void PlayAnimation(int index, int newAnimationType)
        {
            if(animator == null || overrideController == null)
                return;

            if(!animator.gameObject.activeInHierarchy)
                return;

            if(availableAnimations == null || index < 0 || index >= availableAnimations.Length)
                return;

            ResetBase();

            if(newAnimationType == 0)
            {
                baseAnimationToPlay = index;

                overrideController["DwarfM@Idle01"] = availableAnimations[index].clip;

                animator.Play("BaseAnimation", 0, 0f);
                animator.Update(0f);
                
                currentAnimationName = availableAnimations[index].clip.name;
                currentAnimationText.text = currentAnimationName;
            }else{
                upperAnimationToPlay = index;
                
                overrideController["DwarfM@CombatIdle01"] = availableAnimations[index].clip;

                animator.Play("UpperAnimation", 1, 0f);
                animator.Update(0f);
                
                currentUpperAnimationName = availableAnimations[index].clip.name;
                currentAnimationText.text = currentUpperAnimationName+"\n"+currentAnimationName;
            }

            if(currentUpperAnimationName != "")
            {
                currentAnimationText.text = currentUpperAnimationName+"\n"+currentAnimationName;
            }

            character.position = Vector3.zero;
            
            UpdateSpineProxyState();
            
            UpdateProps(index, newAnimationType);
        }
        
        private void UpdateSpineProxyState()
        {
            if(!availableCharacters[currentCharacterIndex].spineProxyBone) return;

            availableCharacters[currentCharacterIndex].spineProxyBone.gameObject.SetActive(upperAnimationToPlay >= 0);
        }
        
        private void ResetProps()
        {
            DisableProps(currentBaseProps, currentUpperProps);
            DisableProps(currentUpperProps, currentBaseProps);

            currentBaseProps = null;
            currentUpperProps = null;
        }

        private void UpdateProps(int activeIndex, int animationLayerType)
        {
            if(availableAnimations == null)
                return;

            DwarfMeleeAnimationEntry entry = availableAnimations[activeIndex];
            GameObject[] newProps = entry != null ? entry.prop : null;

            if(animationLayerType == 0)
            {
                if(currentBaseProps == newProps)
                    return;

                DisableProps(currentBaseProps, currentUpperProps);
                currentBaseProps = newProps;
                EnableProps(currentBaseProps);
            }
            else
            {
                if(currentUpperProps == newProps)
                    return;

                DisableProps(currentUpperProps, currentBaseProps);
                currentUpperProps = newProps;
                EnableProps(currentUpperProps);
            }
        }

        private void EnableProps(GameObject[] props)
        {
            if(props == null)
                return;

            foreach(GameObject prop in props)
            {
                if(prop != null)
                    prop.SetActive(true);
            }
        }

        private void DisableProps(GameObject[] propsToDisable, GameObject[] propsStillUsed)
        {
            if(propsToDisable == null)
                return;

            foreach(GameObject prop in propsToDisable)
            {
                if(prop == null)
                    continue;

                if(IsPropUsedBy(prop, propsStillUsed))
                    continue;

                prop.SetActive(false);
            }
        }

        private bool IsPropUsedBy(GameObject prop, GameObject[] props)
        {
            if(props == null)
                return false;

            foreach(GameObject p in props)
            {
                if(p == prop)
                    return true;
            }

            return false;
        }
        private void CreateAnimationButtons()
        {
            ClearAnimationUI();
            ShowFolder("");
        }
        private void ClearAnimationUI()
        {
            if(availableAnimations != null)
            {
                for(int i = 0; i < availableAnimations.Length; i++)
                {
                    availableAnimations[i].animationButton = null;
                }
            }

            for(int i = animationsUIRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(animationsUIRoot.GetChild(i).gameObject);
            }
            
            clearUpperAnimationButton = null;
            currentSelectedAnimationButton = null;
        }
        private void ShowFolder(string folderPath)
        {
            currentFolder = folderPath;
            ClearAnimationUI();

            if(!string.IsNullOrEmpty(currentFolder))
            {
                CreateBackButton();
            }

            CreateSubFolderButtons(currentFolder);
            CreateAnimationButtonsInFolder(currentFolder);
            SelectCurrentAnimationButtonInOpenFolder();
            
            currentAnimationFolderText.text = "/"+currentFolder;
        }
        private void CreateBackButton()
        {
            GameObject backGO = Instantiate(folderButtonPrefab, animationsUIRoot);

            Text text = backGO.GetComponentInChildren<Text>();
            if(text != null)
            {
                text.text = "← Back";
            }

            Button button = backGO.GetComponent<Button>();
            if(button != null)
            {
                button.onClick.AddListener(() => ShowFolder(GetParentFolder(currentFolder)));
            }

            backGO.SetActive(true);
        }
        private void CreateSubFolderButtons(string folderPath)
        {
            HashSet<string> folders = new HashSet<string>();

            foreach (DwarfMeleeAnimationEntry entry in availableAnimations)
            {
                if(entry == null || entry.clip == null)
                {
                    continue;
                }

                if(!IsDirectChildFolder(entry, folderPath, out string childPath, out string childName))
                {
                    continue;
                }

                folders.Add(childPath);
            }

            foreach(string folder in folders)
            {
                GameObject folderGO = Instantiate(folderButtonPrefab, animationsUIRoot);

                Text text = folderGO.GetComponentInChildren<Text>();
                if(text != null)
                {
                    text.text = GetLastFolderName(folder);
                }

                Button button = folderGO.GetComponent<Button>();
                if(button != null)
                {
                    button.onClick.AddListener(() => ShowFolder(folder));
                }

                folderGO.SetActive(true);
            }
        }
        private void CreateAnimationButtonsInFolder(string folderPath)
        {
            folderPath = NormalizeFolder(folderPath);

            for(int i = 0; i < availableAnimations.Length; i++)
            {
                DwarfMeleeAnimationEntry entry = availableAnimations[i];

                if(entry == null || entry.clip == null)
                {
                    continue;
                }

                string entryPath = NormalizeFolder(entry.folderPath);

                if(entryPath != folderPath)
                {
                    continue;
                }

                int index = i;

                GameObject buttonGO = Instantiate(animationUIPrefab, animationsUIRoot);

                Button button = buttonGO.GetComponent<Button>();
                availableAnimations[i].animationButton = button;
                Image image = buttonGO.GetComponent<Image>();
                Text text = buttonGO.GetComponentInChildren<Text>();

                if(image != null)
                {
                    image.color = normalButtonColor;
                }

                if(text != null)
                {
                    text.color = normalTextColor;
                    text.text = entry.displayName;
                }

                if(button != null)
                {
                    button.onClick.AddListener(() =>
                    {
                        PlayAnimation(index, animationType);
                        SelectAnimationButton(button);
                    });
                }

                buttonGO.SetActive(true);
            }
            
            if(animationType == 1)
            {
                GenerateClearUpperAnimationButton();
            }
        }
        private void SelectCurrentAnimationButtonInOpenFolder()
        {
            if(availableAnimations == null)
                return;

            int selectedIndex = animationType == 0 ? baseAnimationToPlay : upperAnimationToPlay;

            if(selectedIndex < 0 || selectedIndex >= availableAnimations.Length)
                return;

            DwarfMeleeAnimationEntry currentEntry = availableAnimations[selectedIndex];

            if(currentEntry == null)
                return;

            string entryFolder = NormalizeFolder(currentEntry.folderPath);
            string openFolder = NormalizeFolder(currentFolder);

            if(entryFolder != openFolder)
                return;

            SelectAnimationButton(currentEntry.animationButton);
        }
        private void SelectAnimationButton(Button selectedButton)
        {
            if(currentSelectedAnimationButton != null)
            {
                SetButtonSelected(currentSelectedAnimationButton, false);
            }

            currentSelectedAnimationButton = selectedButton;

            if(currentSelectedAnimationButton != null)
            {
                SetButtonSelected(currentSelectedAnimationButton, true);
            }

            if(animationType == 1)
            {
                GenerateClearUpperAnimationButton();
            }
        }
        public int FindAnimationIndex(string clipName)
        {
            if(availableAnimations == null)
            {
                return -1;
            }

            for(int i = 0; i < availableAnimations.Length; i++)
            {
                if(availableAnimations[i] != null && availableAnimations[i].clip != null && availableAnimations[i].clip.name == clipName)
                {
                    return i;
                }
            }

            return -1;
        }
        private GameObject clearUpperAnimationButton = null;
        public void SelectAnimationType(int newType)
        {
            animationType = newType;

            if(newType == 0)
            {
                SetButtonSelected(baseTypeButton, true);
                SetButtonSelected(upperTypeButton, false);

                if(clearUpperAnimationButton != null)
                {
                    Destroy(clearUpperAnimationButton);
                    clearUpperAnimationButton = null;
                }
            }
            else
            {
                SetButtonSelected(baseTypeButton, false);
                SetButtonSelected(upperTypeButton, true);

                GenerateClearUpperAnimationButton();
            }

            SelectCurrentAnimationButtonInOpenFolder();
        }
        private void GenerateClearUpperAnimationButton()
        {
            if(currentUpperAnimationName == "")
            {
                return;
            }
            if(clearUpperAnimationButton == null)
            {
                clearUpperAnimationButton = Instantiate(animationUIPrefab, animationsUIRoot);

                Button button = clearUpperAnimationButton.GetComponent<Button>();
                Image image = clearUpperAnimationButton.GetComponent<Image>();
                Text text = clearUpperAnimationButton.GetComponentInChildren<Text>();

                if(image != null)
                {
                    image.color = normalButtonColor;
                }

                if(text != null)
                {
                    text.color = normalTextColor;
                    text.text = "[X] Clear upper animation";
                }

                if(button != null)
                {
                    button.onClick.AddListener(() =>
                    {
                        ClearUpperAnimation();
                        SelectAnimationButton(button);
                        SetButtonSelected(button, false);
                    });
                }

                clearUpperAnimationButton.SetActive(true);
            }
        }
        public void ClearUpperAnimation()
        {
            DisableProps(currentUpperProps, currentBaseProps);
            currentUpperAnimationName = "";
            currentAnimationText.text = currentAnimationName;
            animator.SetTrigger("ClearUpper");
            currentUpperProps = null;
            upperAnimationToPlay = -1;
            UpdateSpineProxyState();
        }
    ///
        
    ///ANIMATION FOLDER HELPERS
        private bool IsDirectChildFolder(DwarfMeleeAnimationEntry entry, string currentFolder, out string childPath, out string childName)
        {
            childPath = null;
            childName = null;

            string entryPath = NormalizeFolder(entry.folderPath);
            currentFolder = NormalizeFolder(currentFolder);

            if(string.IsNullOrEmpty(entryPath))
            {
                return false;
            }

            string[] entryParts = entryPath.Split('/');

            int currentLevel = string.IsNullOrEmpty(currentFolder)
                ? 0
                : currentFolder.Split('/').Length;

            if(!string.IsNullOrEmpty(currentFolder))
            {
                if(!entryPath.StartsWith(currentFolder + "/"))
                {
                    return false;
                }
            }

            if(entryParts.Length <= currentLevel)
            {
                return false;
            }

            childName = entryParts[currentLevel];

            childPath = string.IsNullOrEmpty(currentFolder) ? childName : currentFolder + "/" + childName;

            return true;
        }
        private string NormalizeFolder(string folder)
        {
            if(string.IsNullOrEmpty(folder))
            {
                return "";
            }

            return folder.Replace("\\", "/").Trim('/');
        }
        private string GetParentFolder(string folder)
        {
            if(string.IsNullOrEmpty(folder))
            {
                return "";
            }

            int index = folder.LastIndexOf('/');
            if(index < 0)
            {
                return "";
            }

            return folder.Substring(0, index);
        }
        private string GetLastFolderName(string folder)
        {
            if(string.IsNullOrEmpty(folder))
            {
                return "";
            }

            int index = folder.LastIndexOf('/');
            if(index < 0)
            {
                return folder;
            }

            return folder.Substring(index + 1);
        }
    ///
    
    ///CAMERA CONTROLS
        public void StartRotateLeft()
        {
            rotationInput = -1f;
            if(leftArrow != null)
            {
                leftArrow.localScale = pressedScale;
            }
        }
        public void StartRotateRight()
        {
            rotationInput = 1f;
            if(rightArrow != null)
            {
                rightArrow.localScale = pressedScale;
            }
        }
        public void StopRotate()
        {
            rotationInput = 0f;

            if(leftArrow != null)
            {
                leftArrow.localScale = normalScale;
            }

            if(rightArrow != null)
            {
                rightArrow.localScale = normalScale;
            }
        }
        public void ZoomIn()
        {
            if(targetCamera == null)
            {
                return;
            }

            targetCamera.fieldOfView = Mathf.Clamp(targetCamera.fieldOfView - zoomStep, minFOV, maxFOV);
            
            zoomIn.localScale = pressedScale;
        }
        public void ZoomOut()
        {
            if(targetCamera == null)
            {
                return;
            }

            targetCamera.fieldOfView = Mathf.Clamp(targetCamera.fieldOfView + zoomStep, minFOV, maxFOV);
            
            zoomOut.localScale = pressedScale;
        }
        public void ZoomUp()
        {
            zoomIn.localScale = normalScale;
            zoomOut.localScale = normalScale;
        }

        
    ///SCENE
        private void ResetBase()
        {
            baseUVRotation = 0f;
            baseOffsetX = 0f;
            baseOffsetY = 0f;
            baseMaterial.mainTextureOffset = Vector2.zero;
            baseMaterial.SetFloat("_UVRotation", 0);
            backgroundMaterial.SetVector("_LightDir", backgroundLightDir);
            character.localEulerAngles = Vector3.zero;
        }
        private void UpdateBaseMaterialOffset()
        {
            if(animator == null || baseMaterial == null)
            {
                return;
            }

            Vector3 localDelta = character.InverseTransformDirection(animator.deltaPosition);

            baseOffsetX -= localDelta.x * baseTreadmillMultiplier;
            baseOffsetY += localDelta.z * baseTreadmillMultiplier;

            float deltaYaw = Mathf.DeltaAngle(0f, -animator.deltaRotation.eulerAngles.y);
            baseUVRotation += deltaYaw;

            if(baseUVRotation > 180f)
            {
                baseUVRotation -= 360f;
            }
            if(baseUVRotation < -180f)
            {
                baseUVRotation += 360f;
            }

            Vector2 offset = baseMaterial.mainTextureOffset;
            offset.x = baseOffsetX;
            offset.y = baseOffsetY;
            baseMaterial.mainTextureOffset = offset;

            baseMaterial.SetFloat("_UVRotation", baseUVRotation);
        }
        private void UpdateBlobShadowFromFeet()
        {
            if(character == null || shadowT == null || shadowSprite == null)
            {
                return;
            }

            Vector3 center = character.position;
            Vector3 left = currentLeftFoot != null ? currentLeftFoot.position : center;
            Vector3 right = currentRightFoot != null ? currentRightFoot.position : center;

            shadowT.position = new Vector3(center.x, shadowFloorY, center.z);

            center.y = shadowFloorY;
            left.y = shadowFloorY;
            right.y = shadowFloorY;

            float leftDistance = Vector3.Distance(center, left);
            float rightDistance = Vector3.Distance(center, right);

            float footScale = Mathf.Max(leftDistance, rightDistance);
            footScale = Mathf.Clamp(footScale + shadowFootPadding, shadowMinScale, shadowMaxScale);

            float distanceY = Mathf.Abs(character.position.y);
            float t = Mathf.Clamp01(distanceY / maxJumpHeight);

            float heightScale = Mathf.Lerp(groundedScale, airScale, t);
            float alpha = Mathf.Lerp(groundedAlpha, airAlpha, t);

            shadowT.localScale = Vector3.one * footScale * heightScale;

            Color c = shadowSprite.color;
            c.a = alpha;
            shadowSprite.color = c;
        }
    ///
        
    ///UI HELPERS
        private void SetButtonSelected(Button button, bool selected)
        {
            Image image = button.GetComponent<Image>();
            Text text = button.GetComponentInChildren<Text>();
            
            if(image != null)
            {
                image.color = selected ? selectedButtonColor : normalButtonColor;
            }

            if(text != null)
            {
                text.color = selected ? selectedTextColor : normalTextColor;
            }
        }
    ///

    ///ANIMATIONS LOAD (EDITOR ONLY)
    #if UNITY_EDITOR
        [HideInInspector][SerializeField] private string animationsFolder = "Assets/Kevin Iglesias/Dwarf Animations/Animations";
        private string[] skipSpineProxyAnimations =
        {
            "DwarfF@Turn01_Left [RM]",
            "DwarfF@Turn01_Right [RM]",
            "DwarfM@Turn01_Left [RM]",
            "DwarfM@Turn01_Right [RM]",
            "DwarfF@CrouchTurn01_R [RM]",
            "DwarfF@CrouchTurn01_L [RM]",
            "DwarfM@CrouchTurn01_R [RM]",
            "DwarfM@CrouchTurn01_L [RM]"
        };

        [HideInInspector][SerializeField] private GameObject propLowChair;
        private string[] propLowChairAnimations =
        {
            "DwarfF@SitLow01 - Begin",
            "DwarfF@SitLow01 - Loop",
            "DwarfF@SitLow01 - Stop",
            "DwarfM@SitLow01 - Begin",
            "DwarfM@SitLow01 - Loop",
            "DwarfM@SitLow01 - Stop"
        };

        [HideInInspector][SerializeField] private GameObject propMediumChair;
        private string[] propMediumChairAnimations =
        {
            "DwarfF@SitMedium01 - Begin",
            "DwarfF@SitMedium01 - Loop",
            "DwarfF@SitMedium01 - Stop",
            "DwarfM@SitMedium01 - Begin",
            "DwarfM@SitMedium01 - Loop",
            "DwarfM@SitMedium01 - Stop"
        };

        [HideInInspector][SerializeField] private GameObject propHighChair;
        private string[] propHighChairAnimations =
        {
            "DwarfF@SitHigh01 - Begin",
            "DwarfF@SitHigh01 - Loop",
            "DwarfF@SitHigh01 - Stop",
            "DwarfM@SitHigh01 - Begin",
            "DwarfM@SitHigh01 - Loop",
            "DwarfM@SitHigh01 - Stop"
        };

        [HideInInspector][SerializeField] private GameObject[] propDrinkablesL;
        private string[] propDrinkablesAnimationsL =
        {
            "DwarfF@Drink01_L",
            "DwarfF@Drink01_L - Loop",
            "DwarfM@Drink01_L",
            "DwarfM@Drink01_L - Loop",
        };
        
        [HideInInspector][SerializeField] private GameObject[] propDrinkablesR;
        private string[] propDrinkablesAnimationsR =
        {
            "DwarfF@Drink01_R",
            "DwarfF@Drink01_R - Loop",
            "DwarfM@Drink01_R",
            "DwarfM@Drink01_R - Loop"
        };

        [HideInInspector][SerializeField] private GameObject[] propEdiblesL;
        private string[] propEdiblesAnimationsL =
        {
            "DwarfF@Eat01_L",
            "DwarfF@Eat01_L - Loop",
            "DwarfM@Eat01_L",
            "DwarfM@Eat01_L - Loop",
        };
        [HideInInspector][SerializeField] private GameObject[] propEdiblesR;
        private string[] propEdiblesAnimationsR =
        {
            "DwarfF@Eat01_R",
            "DwarfF@Eat01_R - Loop",
            "DwarfM@Eat01_R",
            "DwarfM@Eat01_R - Loop"
        };
        [ContextMenu("Populate Animations From Folder")]
        void PopulateAnimationsFromFolder()
        {
            string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { animationsFolder });

            System.Collections.Generic.List<DwarfMeleeAnimationEntry> entries = new();

            foreach(string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);

                if(clip == null || clip.name.StartsWith("__preview__"))
                {
                    continue;
                }

                string folderPath = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
                string root = animationsFolder.Replace("\\", "/");

                string relativeFolder = folderPath.StartsWith(root) ? folderPath.Substring(root.Length).Trim('/') : folderPath;

                string folderName = string.IsNullOrEmpty(relativeFolder) ? "Root" : System.IO.Path.GetFileName(relativeFolder);

                int folderLevel = string.IsNullOrEmpty(relativeFolder) ? 0 : relativeFolder.Split('/').Length;

                entries.Add(new DwarfMeleeAnimationEntry
                {
                    clip = clip,
                    displayName = clip.name,
                    folderName = folderName,
                    folderPath = relativeFolder,
                    folderLevel = folderLevel,

                    prop = GetPropsForAnimation(clip.name),
                    disableSpineProxy = ShouldDisableSpineProxy(clip.name)
                });
            }

            entries.Sort((a, b) =>
            {
                int folderCompare = string.Compare(a.folderPath, b.folderPath, System.StringComparison.Ordinal);
                if (folderCompare != 0) return folderCompare;

                return string.Compare(a.displayName, b.displayName, System.StringComparison.Ordinal);
            });

            availableAnimations = entries.ToArray();

            EditorUtility.SetDirty(this);
        }
        private bool ShouldDisableSpineProxy(string animationName)
        {
            return System.Array.Exists(
                skipSpineProxyAnimations,
                x => x == animationName
            );
        }
        private GameObject[] GetPropsForAnimation(string animationName)
        {
            if (System.Array.Exists(propLowChairAnimations, x => x == animationName))
                return propLowChair != null ? new[] { propLowChair } : System.Array.Empty<GameObject>();

            if (System.Array.Exists(propMediumChairAnimations, x => x == animationName))
                return propMediumChair != null ? new[] { propMediumChair } : System.Array.Empty<GameObject>();

            if (System.Array.Exists(propHighChairAnimations, x => x == animationName))
                return propHighChair != null ? new[] { propHighChair } : System.Array.Empty<GameObject>();

            if (System.Array.Exists(propDrinkablesAnimationsL, x => x == animationName))
                return propDrinkablesL ?? System.Array.Empty<GameObject>();

            if (System.Array.Exists(propDrinkablesAnimationsR, x => x == animationName))
                return propDrinkablesR ?? System.Array.Empty<GameObject>();

            if (System.Array.Exists(propEdiblesAnimationsL, x => x == animationName))
                return propEdiblesL ?? System.Array.Empty<GameObject>();

            if (System.Array.Exists(propEdiblesAnimationsR, x => x == animationName))
                return propEdiblesR ?? System.Array.Empty<GameObject>();

            return System.Array.Empty<GameObject>();
        }
        #endif
    ///
    
    #if UNITY_EDITOR
    
        #if UNITY_EDITOR
        [SerializeField] private AvatarMask upperBodyMask;
        #endif
    
        public void CreateAnimatorControllerFromCurrentAnimations()
        {
            if (availableAnimations == null || baseAnimationToPlay < 0 || baseAnimationToPlay >= availableAnimations.Length)
                return;

            AnimationClip baseClip = availableAnimations[baseAnimationToPlay].clip;
            AnimationClip upperClip = upperAnimationToPlay >= 0 && upperAnimationToPlay < availableAnimations.Length
                ? availableAnimations[upperAnimationToPlay].clip
                : null;

            string lowerName = baseClip != null ? baseClip.name : "LowerAnimation";
            string upperName = upperClip != null ? upperClip.name : "";

            string prefix = lowerName.StartsWith("DwarfF@") || upperName.StartsWith("DwarfF@") ? "DwarfF@" : "DwarfM@";

            lowerName = lowerName.Replace("DwarfF@", "").Replace("DwarfM@", "");
            upperName = upperName.Replace("DwarfF@", "").Replace("DwarfM@", "");

            string controllerName = prefix + lowerName;
            
            if(upperClip != null)
            {
                controllerName = controllerName+" & "+upperName;
            }
            
            
            controllerName = string.Join("", controllerName.Split(System.IO.Path.GetInvalidFileNameChars()));

            string scenePath = SceneManager.GetActiveScene().path;

            string defaultFolder = Path.Combine(
                Path.GetDirectoryName(scenePath),
                "AnimatorControllers",
                "Created"
            ).Replace("\\", "/");

            string sceneFolder = Path.GetDirectoryName(scenePath).Replace("\\", "/");

            string animatorControllersFolder = sceneFolder + "/AnimatorControllers";
            if (!AssetDatabase.IsValidFolder(animatorControllersFolder))
            {
                AssetDatabase.CreateFolder(sceneFolder, "AnimatorControllers");
            }

            string createdFolder = animatorControllersFolder + "/Created";
            if (!AssetDatabase.IsValidFolder(createdFolder))
            {
                AssetDatabase.CreateFolder(animatorControllersFolder, "Created");
            }

            string path = EditorUtility.SaveFilePanelInProject(
                "Create Animator Controller",
                controllerName,
                "controller",
                "Choose where to save the Animator Controller",
                defaultFolder
            );

            if (string.IsNullOrEmpty(path))
                return;

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);

            AnimatorControllerLayer baseLayer = controller.layers[0];

            foreach (var state in baseLayer.stateMachine.states)
            {
                baseLayer.stateMachine.RemoveState(state.state);
            }

            AnimatorState baseState = baseLayer.stateMachine.AddState("BaseAnimation");
            baseState.motion = baseClip;
            baseLayer.stateMachine.defaultState = baseState;

            if (upperClip != null)
            {
                controller.AddLayer("Upper");

                AnimatorControllerLayer[] layers = controller.layers;
                AnimatorControllerLayer upperLayer = layers[layers.Length - 1];

                upperLayer.name = "Upper";
                upperLayer.defaultWeight = 1f;
                upperLayer.blendingMode = AnimatorLayerBlendingMode.Override;
                upperLayer.avatarMask = upperBodyMask;

                foreach (var state in upperLayer.stateMachine.states)
                {
                    upperLayer.stateMachine.RemoveState(state.state);
                }

                AnimatorState upperState = upperLayer.stateMachine.AddState("UpperAnimation");
                upperState.motion = upperClip;
                upperLayer.stateMachine.defaultState = upperState;

                AnimatorStateTransition selfTransition = upperState.AddTransition(upperState);
                selfTransition.hasExitTime = true;
                selfTransition.exitTime = 1f;
                selfTransition.duration = 0f;
                selfTransition.hasFixedDuration = true;
                selfTransition.canTransitionToSelf = true;

                layers[layers.Length - 1] = upperLayer;
                controller.layers = layers;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("Animator Controller created: " + path);
        }
        #endif
    }
}
