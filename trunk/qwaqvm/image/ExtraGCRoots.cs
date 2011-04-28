'From Croquet1.0beta of 11 April 2006 [latest update: #1] on 9 June 2006 at 3:15:32 pm'!"Change Set:		ExtraGCRootsDate:			9 June 2006Author:			Andreas RaabThis change set provides an interface to enable VM plugins to have oop variable locations be updated by the garbage collector.See ExampleCallbackPlugin for an example of use."!Object subclass: #ObjectMemory	instanceVariableNames: 'memory youngStart endOfMemory memoryLimit nilObj falseObj trueObj specialObjectsOop rootTable rootTableCount extraRoots extraRootCount weakRoots weakRootCount child field parentField freeBlock lastHash allocationCount lowSpaceThreshold signalLowSpace compStart compEnd fwdTableNext fwdTableLast remapBuffer remapBufferCount allocationsBetweenGCs tenuringThreshold gcSemaphoreIndex gcBiasToGrow gcBiasToGrowGCLimit gcBiasToGrowThreshold statFullGCs statFullGCMSecs statIncrGCs statIncrGCMSecs statTenures statRootTableOverflows freeContexts freeLargeContexts interruptCheckCounter totalObjectCount shrinkThreshold growHeadroom headerTypeBytes youngStartLocal statMarkCount statSweepCount statMkFwdCount statCompMoveCount statGrowMemory statShrinkMemory statRootTableCount statAllocationCount statSurvivorCount statGCTime statSpecialMarkCount statIGCDeltaTime statpendingFinalizationSignals forceTenureFlag'	classVariableNames: 'AllButHashBits AllButMarkBit AllButMarkBitAndTypeMask AllButRootBit AllButTypeMask BaseHeaderSize BlockContextProto Byte0Mask Byte0Shift Byte1Mask Byte1Shift Byte1ShiftNegated Byte2Mask Byte2Shift Byte3Mask Byte3Shift Byte3ShiftNegated Byte4Mask Byte4Shift Byte4ShiftNegated Byte5Mask Byte5Shift Byte5ShiftNegated Byte6Mask Byte6Shift Byte7Mask Byte7Shift Byte7ShiftNegated Bytes3to0Mask Bytes7to4Mask BytesPerWord CharacterTable ClassArray ClassBitmap ClassBlockContext ClassByteArray ClassCharacter ClassCompiledMethod ClassExternalAddress ClassExternalData ClassExternalFunction ClassExternalLibrary ClassExternalStructure ClassFloat ClassInteger ClassLargeNegativeInteger ClassLargePositiveInteger ClassMessage ClassMethodContext ClassPoint ClassProcess ClassPseudoContext ClassSemaphore ClassString ClassTranslatedMethod CompactClasses CompactClassMask ConstMinusOne ConstOne ConstTwo ConstZero ContextFixedSizePlusHeader CtxtTempFrameStart DoAssertionChecks DoBalanceChecks Done ExternalObjectsArray ExtraRootSize FalseObject GCTopMarker HashBits HashBitsOffset HeaderTypeClass HeaderTypeFree HeaderTypeGC HeaderTypeShort HeaderTypeSizeAndClass LargeContextBit LargeContextSize LongSizeMask MarkBit MethodContextProto NilContext NilObject ProcessSignalingLowSpace RemapBufferSize RootBit RootTableRedZone RootTableSize SchedulerAssociation SelectorAboutToReturn SelectorCannotInterpret SelectorCannotReturn SelectorDoesNotUnderstand SelectorMustBeBoolean SelectorRunWithIn ShiftForWord Size4Bit SizeMask SmallContextSize SpecialSelectors StackStart StartField StartObj TheDisplay TheFinalizationSemaphore TheInputSemaphore TheInterruptSemaphore TheLowSpaceSemaphore TheTimerSemaphore TrueObject TypeMask Upward WordMask'	poolDictionaries: ''	category: 'VMMaker-Interpreter'!!ObjectMemory methodsFor: 'gc -- mark and sweep' stamp: 'ar 6/9/2006 14:56'!markPhase	"Mark phase of the mark and sweep garbage collector. Set 	the mark bits of all reachable objects. Free chunks are 	untouched by this process."	"Assume: All non-free objects are initially unmarked. Root 	objects were unmarked when they were made roots. (Make 	sure this stays true!!!!)."	| oop |	self inline: false.	"clear the recycled context lists"	freeContexts := NilContext.	freeLargeContexts := NilContext.	"trace the interpreter's objects, including the active stack 	and special objects array"	self markAndTraceInterpreterOops.	statSpecialMarkCount := statMarkCount.	"trace the roots"	1 to: rootTableCount do: [:i | 			oop := rootTable at: i.			self markAndTrace: oop].	1 to: extraRootCount do:[:i|			oop := (extraRoots at: i) at: 0.			(self isIntegerObject: oop) ifFalse:[self markAndTrace: oop]].! !!ObjectMemory methodsFor: 'initialization' stamp: 'ar 6/8/2006 12:34'!initializeObjectMemory: bytesToShift	"Initialize object memory variables at startup time. Assume endOfMemory is initially set (by the image-reading code) to the end of the last object in the image. Initialization redefines endOfMemory to be the end of the object allocation area based on the total available memory, but reserving some space for forwarding blocks."	"Assume: image reader initializes the following variables:		memory		endOfMemory		memoryLimit		specialObjectsOop		lastHash	"	"di 11/18/2000 fix slow full GC"	self inline: false.	"set the start of the young object space"	youngStart := endOfMemory.	"image may be at a different address; adjust oops for new location"	totalObjectCount := self adjustAllOopsBy: bytesToShift.	self initializeMemoryFirstFree: endOfMemory. "initializes endOfMemory, freeBlock"	specialObjectsOop := specialObjectsOop + bytesToShift.	"heavily used special objects"	nilObj	:= self splObj: NilObject.	falseObj	:= self splObj: FalseObject.	trueObj	:= self splObj: TrueObject.	rootTableCount := 0.	freeContexts := NilContext.	freeLargeContexts := NilContext.	allocationCount := 0.	lowSpaceThreshold := 0.	signalLowSpace := false.	compStart := 0.	compEnd := 0.	fwdTableNext := 0.	fwdTableLast := 0.	remapBufferCount := 0.	allocationsBetweenGCs := 4000.  "do incremental GC after this many allocations"	tenuringThreshold := 2000.  "tenure all suriving objects if count is over this threshold"	growHeadroom := 4*1024*1024. "four megabyte of headroom when growing"	shrinkThreshold := 8*1024*1024. "eight megabyte of free space before shrinking"	"garbage collection statistics"	statFullGCs := 0.	statFullGCMSecs := 0.	statIncrGCs := 0.	statIncrGCMSecs := 0.	statTenures := 0.	statRootTableOverflows := 0.	statGrowMemory := 0.	statShrinkMemory := 0.	forceTenureFlag := 0.	gcBiasToGrow := 0.	gcBiasToGrowGCLimit := 0.	extraRootCount := 0.! !!ObjectMemory methodsFor: 'gc -- compaction' stamp: 'ar 6/8/2006 11:55'!mapPointersInObjectsFrom: memStart to: memEnd	"Use the forwarding table to update the pointers of all non-free objects in the given range of memory. Also remap pointers in root objects which may contains pointers into the given memory range, and don't forget to flush the method cache based on the range"	| oop |	self inline: false.	self compilerMapHookFrom: memStart to: memEnd.	"update interpreter variables"	self mapInterpreterOops.	1 to: extraRootCount do:[:i |		oop := (extraRoots at: i) at: 0.		(self isIntegerObject: oop) ifFalse:[(extraRoots at: i) at: 0 put: (self remap: oop)]].	self flushMethodCacheFrom: memStart to: memEnd.	self updatePointersInRootObjectsFrom: memStart to: memEnd.	self updatePointersInRangeFrom: memStart to: memEnd.! !!ObjectMemory methodsFor: 'gc -- compaction' stamp: 'ar 6/8/2006 11:54'!updatePointersInRootObjectsFrom: memStart to: memEnd 	"update pointers in root objects"	| oop |	self inline: false.	1 to: rootTableCount do: [:i | 			oop := rootTable at: i.			(oop < memStart or: [oop >= memEnd])				ifTrue: ["Note: must not remap the fields of any object twice!!"					"remap this oop only if not in the memory range 					covered below"					self remapFieldsAndClassOf: oop]].! !!ObjectMemory methodsFor: 'plugin support' stamp: 'ar 6/8/2006 13:06'!addGCRoot: varLoc	"Add the given variable location to the extra roots table"	self export: true.	self var: #varLoc declareC: 'sqInt *varLoc'.	extraRootCount >= ExtraRootSize ifTrue:[^false]. "out of space"	extraRoots at: (extraRootCount := extraRootCount+1) put: varLoc.	^true! !!ObjectMemory methodsFor: 'plugin support' stamp: 'ar 6/8/2006 13:06'!removeGCRoot: varLoc	"Remove the given variable location to the extra roots table"	| root |	self export: true.	self var: #varLoc declareC: 'sqInt *varLoc'.	self var: #root declareC:'sqInt *root'.	1 to: extraRootCount do:[:i|		root := extraRoots at: i.		root == varLoc ifTrue:["swap varLoc with last entry"			extraRoots at: i put: (extraRoots at: extraRootCount).			extraRootCount := extraRootCount-1.			^true]].	^false "not found"! !!ObjectMemory class methodsFor: 'translation' stamp: 'ar 6/8/2006 12:36'!declareCVarsIn: aCCodeGenerator	aCCodeGenerator var: #memory type:#'usqInt'.	aCCodeGenerator		var: #remapBuffer		declareC: 'sqInt remapBuffer[', (RemapBufferSize + 1) printString, ']'.	aCCodeGenerator		var: #rootTable		declareC: 'sqInt rootTable[', (RootTableSize + 1) printString, ']'.	"Weak roots must be large enough for roots+remapBuffer+sizeof(allCallsOn: #markAndTrace:)"	aCCodeGenerator		var: #weakRoots		declareC: 'sqInt weakRoots[', (RootTableSize + RemapBufferSize + 100) printString, ']'.	aCCodeGenerator		var: #extraRoots		declareC: 'sqInt* extraRoots[', (ExtraRootSize + 1) printString, ']'.	aCCodeGenerator		var: #headerTypeBytes		declareC: 'sqInt headerTypeBytes[4]'.		aCCodeGenerator var: #youngStart type: 'usqInt'.	aCCodeGenerator var: #endOfMemory type: 'usqInt'.	aCCodeGenerator var: #memoryLimit type: 'usqInt'.	aCCodeGenerator var: #youngStartLocal type: 'usqInt'.! !!ObjectMemory class methodsFor: 'initialization' stamp: 'ar 6/8/2006 12:35'!initializeWithBytesToWord:  numberOfBytesInAWord	"ObjectMemory initializeWithBytesToWord: Smalltalk wordSize"	self initBytesPerWord: numberOfBytesInAWord.	"Translation flags (booleans that control code generation via conditional translation):"	DoAssertionChecks := false.  "generate assertion checks"	DoBalanceChecks := false. "generate stack balance checks"	self initializeSpecialObjectIndices.	self initializeObjectHeaderConstants.	CtxtTempFrameStart := 6.  "Copy of TempFrameStart in Interp"	ContextFixedSizePlusHeader := CtxtTempFrameStart + 1.	SmallContextSize := ContextFixedSizePlusHeader + 16 * BytesPerWord.  "16 indexable fields"	"Large contexts have 56 indexable fileds.  Max with single header word."	"However note that in 64 bits, for now, large contexts have 3-word headers"	LargeContextSize := ContextFixedSizePlusHeader + 56 * BytesPerWord.		LargeContextBit := 16r40000.  "This bit set in method headers if large context is needed."	NilContext := 1.  "the oop for the integer 0; used to mark the end of context lists"	RemapBufferSize := 25.	RootTableSize := 2500.  	"number of root table entries (4 bytes/entry)"	RootTableRedZone := RootTableSize - 100.	"red zone of root table - when reached we force IGC"	"tracer actions"	StartField := 1.	StartObj := 2.	Upward := 3.	Done := 4.	ExtraRootSize := 2048. "max. # of external roots"! !